using Pragmatic.Testing.Assertions;
using Showcase.Booking.Entities;
using Showcase.Booking.Enums;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
/// Tests the Reservation state machine (anemic model): transitions go through the SG-generated TransitionTo,
/// and events on entering a state are auto-raised by [RaisesEvent&lt;T&gt;] on the target enum member. Events
/// that depend on caller input (ReservationCancelled) or persistence (ReservationCreated) are raised by the
/// operation/lifecycle, not the entity — asserted as such here.
/// </summary>
public class ReservationTests
{
    [Fact]
    public void Create_SetsDefaultStatus_ToPending()
    {
        var reservation = CreatePendingReservation();

        reservation.Status.Should().Be(ReservationStatus.Pending);
    }

    [Fact]
    public void Create_DoesNotRaiseSynchronously_LifecycleRaisesOnPersist()
    {
        // ReservationCreated is raised on persist by [Raises<ReservationCreated>] (lifecycle), not by the factory.
        var reservation = CreatePendingReservation();

        reservation.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Confirm_TransitionsToConfirmed()
    {
        var reservation = CreatePendingReservation();

        reservation.TransitionTo(ReservationStatus.Confirmed);

        reservation.Status.Should().Be(ReservationStatus.Confirmed);
    }

    [Fact]
    public void Confirm_RaisesReservationConfirmedEvent_WithAllData()
    {
        var reservation = CreatePendingReservation();
        reservation.ClearDomainEvents();

        reservation.TransitionTo(ReservationStatus.Confirmed);

        var @event = reservation.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<Booking.Events.ReservationConfirmed>()
            .Subject;

        // Event must carry all data needed by downstream consumers — filled by name from entity members.
        @event.ReservationId.Should().Be(reservation.Id);
        @event.GuestId.Should().Be(reservation.GuestId);
        @event.PropertyId.Should().Be(reservation.PropertyId);
        @event.TotalAmount.Should().Be(reservation.TotalAmount);
        @event.Currency.Should().Be(reservation.Currency);
    }

    [Fact]
    public void CheckIn_TransitionsToCheckedIn()
    {
        var reservation = CreatePendingReservation();
        reservation.TransitionTo(ReservationStatus.Confirmed);
        reservation.ClearDomainEvents();

        reservation.SetCheckedInBy("system");
        reservation.TransitionTo(ReservationStatus.CheckedIn);

        reservation.Status.Should().Be(ReservationStatus.CheckedIn);
    }

    [Fact]
    public void CheckIn_RaisesGuestCheckedInEvent_WithCheckedInBy()
    {
        var reservation = CreatePendingReservation();
        reservation.TransitionTo(ReservationStatus.Confirmed);
        reservation.ClearDomainEvents();

        reservation.SetCheckedInBy("system");
        reservation.TransitionTo(ReservationStatus.CheckedIn);

        reservation.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<Booking.Events.GuestCheckedIn>()
            .Which.CheckedInBy.Should().Be("system");
    }

    [Fact]
    public void MarkPaymentReceived_TransitionsToPaymentReceived()
    {
        var reservation = CreatePendingReservation();
        reservation.TransitionTo(ReservationStatus.Confirmed);

        reservation.TransitionTo(ReservationStatus.PaymentReceived);

        reservation.Status.Should().Be(ReservationStatus.PaymentReceived);
    }

    [Fact]
    public void Cancel_TransitionsToCancelled()
    {
        var reservation = CreatePendingReservation();

        reservation.TransitionTo(ReservationStatus.Cancelled);

        reservation.Status.Should().Be(ReservationStatus.Cancelled);
    }

    [Fact]
    public void Cancel_DoesNotRaiseOnEntity_MutationRaisesItWithReason()
    {
        // ReservationCancelled carries the caller's reason (not an entity member), so it is raised by
        // [Raises<ReservationCancelled>] on CancelReservationMutation — the bare transition raises nothing.
        var reservation = CreatePendingReservation();
        reservation.ClearDomainEvents();

        reservation.TransitionTo(ReservationStatus.Cancelled);

        reservation.DomainEvents.Should().NotContain(e => e is Booking.Events.ReservationCancelled);
    }

    private static Reservation CreatePendingReservation()
    {
        return Reservation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(3),
            2, 300m, "EUR");
    }
}
