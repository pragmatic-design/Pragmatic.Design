namespace Showcase.Booking.Reservations.Mutations;

/// <summary>
/// Marks a reservation as having received payment.
/// Demonstrates: Cross-boundary Mutation&lt;T&gt; invoked via IBookingActions —
/// Billing calls this mutation through the boundary interface after invoice payment.
/// Also exposed as HTTP endpoint for direct API access.
/// </summary>
[Endpoint(HttpVerb.Post, "/{id}/payment-received")]
[EndpointGroup<ReservationsGroup>]
[RequirePermission(BookingPermissions.Reservation.Update)]
[Mutation(Mode = MutationMode.Update)]
public partial class MarkPaymentReceivedMutation : Mutation<Reservation, NotFoundError>
{
    public required Guid Id { get; init; }

    public override Task<Result<Reservation, IError>> ApplyAsync(
        Reservation entity, CancellationToken ct = default)
    {
        var result = entity.TransitionTo(ReservationStatus.PaymentReceived);
        if (result.IsFailure)
            return Task.FromResult(Result<Reservation, IError>.Failure(result.Error));

        return Task.FromResult(Result<Reservation, IError>.Success(entity));
    }
}
