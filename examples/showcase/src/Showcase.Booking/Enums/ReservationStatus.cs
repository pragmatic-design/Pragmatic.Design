using Pragmatic;

namespace Showcase.Booking.Enums;

[FastEnum]
public enum ReservationStatus
{
    [InitialState]
    Pending,

    // Anemic model: TransitionTo(Confirmed) auto-raises ReservationConfirmed — every field is an entity member.
    [TransitionFrom(ReservationStatus.Pending)]
    [RaisesEvent<ReservationConfirmed>]
    Confirmed,

    [TransitionFrom(ReservationStatus.Confirmed)]
    PaymentReceived,

    // GuestCheckedIn's checkedInBy binds to entity.CheckedInBy, set by the mutation before the transition.
    [TransitionFrom(ReservationStatus.Confirmed)]
    [TransitionFrom(ReservationStatus.PaymentReceived)]
    [RaisesEvent<GuestCheckedIn>]
    CheckedIn,

    [TransitionFrom(ReservationStatus.CheckedIn)]
    CheckedOut,

    [TransitionFrom(ReservationStatus.Pending)]
    [TransitionFrom(ReservationStatus.Confirmed)]
    [TransitionFrom(ReservationStatus.PaymentReceived)]
    Cancelled,

    [TransitionFrom(ReservationStatus.Pending)]
    [TransitionFrom(ReservationStatus.Confirmed)]
    NoShow
}
