using Pragmatic.Authoring;

namespace Showcase.Booking.Reservations.Mutations;

/// <summary>
/// Cancels an existing reservation with a reason.
/// Demonstrates:
/// - Mutation&lt;T&gt; with input validation ([Required] on Reason)
/// - State machine transition via entity.Cancel()
/// - IConfigurationStore for tenant-specific cancellation window
///   (premium tenants get 72h, base default is 24h)
/// MutationInvoker validates input before loading entity.
/// </summary>
[Endpoint(HttpVerb.Post, "/{id}/cancel")]
[EndpointGroup<ReservationsGroup>]
[RequirePermission(BookingPermissions.Reservation.Update)]
[Mutation(Mode = MutationMode.Update)]
// The two rules ApplyAsync below enforces, written for whoever asked for them rather than for the
// compiler. They reach Showcase.Booking.Generated.PragmaticUseCases, which is a traceability
// document that cannot drift from the code because this compilation writes it.
[UseCase("BKG-CANCEL", Title = "Cancel a reservation")]
[Rule("A reservation can be cancelled only before its tenant's cancellation window closes")]
[Rule("Cancelling a reservation tells the rest of the system, by raising ReservationCancelled")]
// Anemic: the mutation transitions, and [Raises<ReservationCancelled>] auto-raises the event — its ctor is
// filled by name from the entity (ReservationId->Id, GuestId, PropertyId) and the mutation input (Reason).
[Raises<ReservationCancelled>]
public partial class CancelReservationMutation : Mutation<Reservation, ConflictError>
{
    private IConfigurationStore _configStore = null!;
    private ITenantContext _tenantContext = null!;
    private IClock _clock = null!;

    public required Guid Id { get; init; }

    [Required]
    [Pragmatic.Mapping.Attributes.MapIgnore]
    public required string Reason { get; init; }

    public override async Task<Result<Reservation, IError>> ApplyAsync(
        Reservation entity, CancellationToken ct = default)
    {
        // Read tenant-specific cancellation window from IConfigurationStore
        // Base default: 24h. Tenant "premium-hotel": 72h. Tenant "grand-resort": 48h.
        // Demonstrates: IConfigurationStore with tenant-scoped overrides
        var windowValue = _tenantContext.IsResolved
            ? await _configStore.GetAsync("Booking:CancellationWindowHours", _tenantContext.TenantId!, ct).ConfigureAwait(false)
            : null;

        // Fall back to base value if no tenant override
        windowValue ??= await _configStore.GetAsync("Booking:CancellationWindowHours", ct).ConfigureAwait(false);

        var cancellationWindowHours = int.TryParse(windowValue, out var hours) ? hours : 24;

        // Enforce cancellation window — must cancel before cutoff
        var cutoff = entity.CheckIn.AddHours(-cancellationWindowHours);
        if (_clock.UtcNow > cutoff)
            return new ConflictError
            {
                EntityType = nameof(Reservation),
                EntityId = entity.Id.ToString(),
                Reason = $"Cancellation window expired. Must cancel at least {cancellationWindowHours}h before check-in ({cutoff:yyyy-MM-dd HH:mm} UTC)."
            };

        var result = entity.TransitionTo(ReservationStatus.Cancelled);
        if (result.IsFailure)
            return Result<Reservation, IError>.Failure(result.Error);

        return Result<Reservation, IError>.Success(entity);
    }
}
