using Pragmatic.Ensure;
using Pragmatic.Temporal.Attributes;
using Showcase.Booking.Infrastructure.FeatureFlags;

namespace Showcase.Booking.Reservations.Mutations;

/// <summary>
/// Checks in a guest for their reservation, optionally assigning a physical room.
/// Demonstrates:
/// - Mutation&lt;T&gt; with state machine transition
/// - Ensure guards for programming contracts
/// - ICurrentUser for audit trail (checkedInBy)
/// - Creating a related entity (RoomAssignment) within ApplyAsync — same UoW/DbContext
/// - Feature flags: "early-check-in" gated by tenant targeting
/// MutationInvoker handles entity loading and persistence.
/// </summary>
[Endpoint(HttpVerb.Post, "/{id}/check-in")]
[EndpointGroup<ReservationsGroup>]
[RequirePermission(BookingPermissions.Reservation.Update)]
[Mutation(Mode = MutationMode.Update)]
public partial class CheckInGuestMutation : Mutation<Reservation, ConflictError>
{
    // Injected by Endpoints SG — scoped per-request
    private ICurrentUser _currentUser = null!;

    /// <summary>
    /// Repository for creating the room assignment record.
    /// Both Reservation and RoomAssignment belong to BookingBoundary → same DbContext →
    /// same UoW → saved in one SaveChangesAsync call.
    /// </summary>
    private IRepository<RoomAssignment> _roomAssignments = null!;

    /// <summary>
    /// Feature flags — checks if early check-in is allowed for the current tenant.
    /// Demonstrates: IFeatureFlags, which resolves the evaluation context from the ambient
    /// IFeatureFlagContextProvider so the call site does not have to.
    /// </summary>
    private IFeatureFlags _featureFlags = null!;

    public required Guid Id { get; init; }

    /// <summary>
    /// Physical room number to assign at check-in (e.g. "204", "Suite 12").
    /// If omitted, check-in proceeds without a room assignment record.
    /// </summary>
    [Pragmatic.Mapping.Attributes.MapIgnore]
    public string? RoomNumber { get; init; }

    /// <summary>
    /// When the guest actually walked in, as the front desk types it: the hotel's wall clock, not the
    /// clerk's browser and not UTC. [FromBusinessTimezone] reads it in the zone the host declared
    /// (UseBusinessTimeZone), so the same "21:45" means the same instant whoever is on the desk.
    /// </summary>
    /// <remarks>
    /// ⚠️ DateTime and not DateTimeOffset: an offset on the wire decides the instant by itself and
    /// leaves the attribute nothing to do. Compare CreateReservationRequest.ExpectedArrival, which is
    /// the same shape read against the *caller's* zone instead of the business one.
    /// </remarks>
    [Pragmatic.Mapping.Attributes.MapIgnore]
    [FromBusinessTimezone]
    public DateTime? ActualArrival { get; init; }


    public override async Task<Result<Reservation, IError>> ApplyAsync(
        Reservation entity, CancellationToken ct = default)
    {
        // Ensure: programming contract — entity must exist (MutationInvoker guarantees this,
        // but explicit guard documents the expectation and catches DI issues early)
        Ensure.ThrowIfNull(entity);

        // Feature flag: "early-check-in" — only premium tenants can check in before the scheduled date.
        // If the flag is disabled for this tenant and check-in is before the scheduled date,
        // the check-in is rejected with a ConflictError.
        // Demonstrates: IFeatureFlags resolves the tenant/user context itself — no context plumbing here.
        if (entity.CheckIn.Date > DateTimeOffset.UtcNow.Date)
        {
            var earlyCheckInAllowed = await _featureFlags.IsEnabledAsync<EarlyCheckInFlag>(ct).ConfigureAwait(false);

            if (!earlyCheckInAllowed)
                return new ConflictError
                {
                    EntityType = nameof(Reservation),
                    EntityId = entity.Id.ToString(),
                    Reason = $"Early check-in not available. Check-in is scheduled for {entity.CheckIn:yyyy-MM-dd}."
                };
        }

        // ICurrentUser: captures who performed the check-in for the audit trail
        var checkedInBy = _currentUser.IsAuthenticated ? _currentUser.Id : "system";

        // Anemic: set the audit field, then transition; [RaisesEvent<GuestCheckedIn>] reads entity.CheckedInBy.
        entity.SetCheckedInBy(checkedInBy);

        // Already an instant when it gets here — see the remark on ActualArrival.
        if (ActualArrival is { } actualArrival)
            entity.SetActualArrival(
                new DateTimeOffset(DateTime.SpecifyKind(actualArrival, DateTimeKind.Utc), TimeSpan.Zero));

        var result = entity.TransitionTo(ReservationStatus.CheckedIn);
        if (result.IsFailure)
            return Result<Reservation, IError>.Failure(result.Error!);

        // If a room number was provided, create a RoomAssignment in the same UoW.
        // Both entities share BookingDbContext → one SaveChangesAsync covers both.
        if (!string.IsNullOrWhiteSpace(RoomNumber))
        {
            var assignment = RoomAssignment.Create(entity.Id, entity.GuestId, RoomNumber, DateTimeOffset.UtcNow);
            _roomAssignments.Add(assignment);
        }

        return Result<Reservation, IError>.Success(entity);
    }
}
