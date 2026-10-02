using Pragmatic.Authorization.Policy;
using Showcase.Booking.Infrastructure.Authorization;
using Showcase.Booking.Infrastructure.FeatureFlags;
using Showcase.Booking.Errors;
using Showcase.Catalog.Entities;

namespace Showcase.Booking.Reservations.Actions;

/// <summary>
/// Creates a new reservation, checking room availability and occupancy.
/// Demonstrates: [Endpoint] on DomainAction with [Validate] triggering async validation.
/// Boundary assignment is inferred from namespace (Showcase.Booking.Actions → BookingBoundary).
/// DTO-level validation (FutureDate, GreaterThanProperty, Range) runs first (sync),
/// then async validator checks room availability against DB.
/// Also demonstrates: API versioning — V2 adds loyalty member discount via [SinceVersion] + ExecuteV2.
/// </summary>
[DomainAction]
// The rows the action needs, read once by the invoker: an unknown one is a 404 before the body runs,
// and the rules that need them are ValidateLoadedAsync below. The keys live inside the request, which
// is what [LoadEntity] can name.
[LoadEntity<Property>("Request.PropertyId")]
[LoadEntity<RoomType>("Request.RoomTypeId")]
[RequirePolicy<ReservationManagementPolicy>]
[Endpoint(HttpVerb.Post, "api/reservations")]
[ApiSummary("Create Reservation")]
[ApiDescription("Creates a new reservation for a guest at a hotel property.")]
[ApiTags("Reservations")]
[Validate]
public partial class CreateReservationAction : DomainAction<Guid, RoomUnavailableError>
{
    private IRepository<Reservation> _reservations = null!;

    /// <summary>
    /// Demonstrates: IClock injection for testable time — no more DateTimeOffset.UtcNow.
    /// </summary>
    private IClock _clock = null!;

    /// <summary>
    /// Feature flags — used to gate the loyalty discount behind a percentage rollout.
    /// Demonstrates: IFeatureFlagStore with an *explicit* context — the flag is evaluated for the guest of
    /// the reservation, not for the caller. Compare with CheckInGuestMutation, which uses IFeatureFlags to
    /// evaluate against the ambient tenant/user. Use the store directly whenever the subject of the
    /// decision is not the current principal.
    /// </summary>
    private IFeatureFlagStore _featureFlags = null!;

    public required CreateReservationRequest Request { get; init; }

    /// <summary>Loyalty program member ID — V2 adds 10% loyalty discount.</summary>
    [SinceVersion("2.0")]
    public string? LoyaltyMemberId { get; init; }

    /// <summary>
    /// V1: the loyalty discount is behind a percentage rollout, evaluated for the guest.
    /// </summary>
    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
        => ReserveAsync(discountNeedsTheRollout: true, ct);

    /// <summary>
    /// V2: Applies a 10% loyalty discount when a LoyaltyMemberId is provided.
    /// Demonstrates: ExecuteV2 convention — the SG generates a separate versioned endpoint.
    /// </summary>
    public Task<Result<Guid, IError>> ExecuteV2(CancellationToken ct = default)
        => ReserveAsync(discountNeedsTheRollout: false, ct);

    /// <summary>
    /// What both versions do: one read of the property and of the room type, one copy of each refusal.
    /// The versions differ only in whether the loyalty discount waits for the rollout.
    /// </summary>
    private async Task<Result<Guid, IError>> ReserveAsync(bool discountNeedsTheRollout, CancellationToken ct)
    {
        // _property and _roomType are the rows the invoker preloaded: no read here, and no 404 to write.
        var roomType = _roomType;

        if (Request.NumberOfGuests > roomType.MaxOccupancy)
            return new RoomUnavailableError
            {
                PropertyId = Request.PropertyId,
                RoomTypeId = Request.RoomTypeId,
                CheckIn = Request.CheckIn,
                CheckOut = Request.CheckOut
            };

        // Calculate total — IClock used for audit timestamp
        var now = _clock.UtcNow;
        var nights = (int)(Request.CheckOut - Request.CheckIn).TotalDays;
        var totalAmount = nights * roomType.BaseRate;

        // Feature flag: loyalty discount (percentage rollout to 30% of users). V1 applies the 10% only when
        // the rollout reaches the guest — evaluated against the guest's user context for deterministic
        // bucketing; V2 applies it whenever a member ID is given.
        if (!string.IsNullOrEmpty(LoyaltyMemberId)
            && (!discountNeedsTheRollout || await RolloutReachesTheGuestAsync(ct).ConfigureAwait(false)))
            totalAmount *= 0.90m;

        var reservation = Reservation.Create(
            Request.GuestId, Request.PropertyId, Request.RoomTypeId,
            Request.CheckIn, Request.CheckOut, Request.NumberOfGuests,
            totalAmount, roomType.Currency,
            Request.SpecialRequests);

        // Use clock-based time for audit
        reservation.CreatedAt = now;

        // The wall clock the guest typed has already become an instant by the time it gets here:
        // [FromClientTimezone] on the request property read it in the zone the caller declared, so
        // what arrives is UTC. Specifying the kind rather than calling ToUniversalTime() is the point —
        // if the conversion had not happened the stored value would be visibly wrong, which is what the
        // integration test removes the attribute to see.
        if (Request.ExpectedArrival is { } expectedArrival)
            reservation.SetExpectedArrival(
                new DateTimeOffset(DateTime.SpecifyKind(expectedArrival, DateTimeKind.Utc), TimeSpan.Zero));

        _reservations.Add(reservation);

        return reservation.Id;
    }

    /// <summary>
    ///     The room has to be free for the dates asked. A rule on a row the invoker already loaded, run
    ///     right after the load and before the body.
    /// </summary>
    /// <remarks>
    ///     It was a <c>CreateReservationValidator</c> implementing <c>IAsyncValidator</c>, which read the
    ///     room type a second time: a validator runs before the load and cannot see the action's loaded
    ///     entities.
    /// </remarks>
    private async Task<Pragmatic.Validation.Types.ValidationError> ValidateLoadedAsync(CancellationToken ct = default)
    {
        var overlapping = await _reservations.CountAsync(
            ReservationSpecifications.Overlapping(
                Request.PropertyId, Request.RoomTypeId, Request.CheckIn, Request.CheckOut),
            ct).ConfigureAwait(false);

        return overlapping >= _roomType.TotalRooms
            ? Pragmatic.Validation.Types.ValidationError.For("RoomTypeId", "validation.room.unavailable")
            : Pragmatic.Validation.Types.ValidationError.Valid;
    }

    private Task<bool> RolloutReachesTheGuestAsync(CancellationToken ct)
        => _featureFlags.IsEnabledAsync<LoyaltyDiscountFlag>(
            new FeatureFlagContext { UserId = Request.GuestId.ToString() }, ct);
}
