using Pragmatic.Validation;
using ValidationResult = Pragmatic.Validation.Types.ValidationError;

namespace Showcase.Booking.Validators;

/// <summary>
/// Async validator for CreateReservationRequest: one guest, one stay at a time.
/// </summary>
/// <remarks>
///     <para>
///         Demonstrates: <c>IAsyncValidator&lt;T&gt;</c> with a database read, declared with
///         <c>[Validator]</c> and executed because the action carries <c>[Validate]</c>. The registration
///         in the container is generated.
///     </para>
///     <para>
///         ⚠️ This is the shape an async validator is <b>for</b>: a rule about rows the operation does not
///         load. A validator runs before the loads and cannot see the action's loaded entities, so a
///         rule about a loaded row would read it a second time. "The room type exists" is therefore a
///         <c>[LoadEntity]</c> on the action (a 404 from the invoker), and "the room is free" is its
///         <c>ValidateLoadedAsync</c>, on the row already read.
///     </para>
/// </remarks>
[Validator]
public class CreateReservationValidator(IReadRepository<Reservation> reservations)
    : IAsyncValidator<CreateReservationRequest>
{
    public async Task<ValidationResult> ValidateAsync(
        CreateReservationRequest request,
        CancellationToken ct = default)
    {
        var guestId = request.GuestId;
        var checkIn = request.CheckIn;
        var checkOut = request.CheckOut;

        var alreadyStaying = ReservationSpecifications.IsActive()
                             & ReservationSpecifications.ForGuest(guestId)
                             & Spec<Reservation>.Where(r => r.CheckIn < checkOut && r.CheckOut > checkIn);

        return await reservations.ExistsAsync(alreadyStaying, ct).ConfigureAwait(false)
            ? ValidationResult.For(nameof(CreateReservationRequest.GuestId), "validation.guest.already_booked")
            : ValidationResult.Valid;
    }
}
