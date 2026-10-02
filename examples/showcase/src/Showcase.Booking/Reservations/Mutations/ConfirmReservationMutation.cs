using Pragmatic.Authoring;

namespace Showcase.Booking.Reservations.Mutations;

/// <summary>
/// Confirms a pending reservation.
/// Demonstrates: a state-machine move with no body at all — [TransitionsTo] is performed by the generated
/// invoker, which answers 409 when the reservation is not pending; [RaisesEvent&lt;ReservationConfirmed&gt;]
/// on the enum raises the event. MutationInvoker handles entity loading and persistence.
/// </summary>
[Endpoint(HttpVerb.Post, "/{id}/confirm")]
[EndpointGroup<ReservationsGroup>]
[RequirePermission(BookingPermissions.Reservation.Update)]
[Mutation(Mode = MutationMode.Update)]
[TransitionsTo<ReservationStatus>(ReservationStatus.Confirmed)]
[UseCase("BKG-CONFIRM", Title = "Confirm a pending reservation")]
[Rule("Only a pending reservation can be confirmed")]
public partial class ConfirmReservationMutation : Mutation<Reservation, ConflictError>
{
    public required Guid Id { get; init; }
}
