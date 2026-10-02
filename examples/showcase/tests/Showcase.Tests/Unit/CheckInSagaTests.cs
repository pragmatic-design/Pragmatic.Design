using Pragmatic.Testing.Assertions;
using Pragmatic.Messaging.Saga;
using Showcase.Booking.Reservations.Sagas;

namespace Showcase.Tests.Unit;

/// <summary>
///     Tests for CheckInSaga state machine logic (handler methods, state transitions).
///     The SG-generated orchestrator wires these handlers; here we test the handlers directly.
/// </summary>
public class CheckInSagaTests
{
    [Fact]
    public void Handle_GuestArrived_SetsGuestVerifiedState()
    {
        var saga = new CheckInSaga();
        var reservationId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        var action = saga.Handle(new GuestArrived(reservationId, guestId, DateTimeOffset.UtcNow));

        saga.State.Should().Be(CheckInState.GuestVerified);
        saga.ReservationId.Should().Be(reservationId);
        action.Should().NotBeNull();
        action.ReservationId.Should().Be(reservationId);
        action.GuestId.Should().Be(guestId);
    }

    [Fact]
    public void Handle_IdentityVerified_Valid_ReturnsAssignRoomAction()
    {
        var saga = new CheckInSaga { State = CheckInState.GuestVerified, ReservationId = Guid.NewGuid() };

        var action = saga.Handle(new IdentityVerified(saga.ReservationId, IsValid: true));

        action.Should().NotBeNull();
        action.ReservationId.Should().Be(saga.ReservationId);
    }

    /// <summary>
    ///     A failed verification is a business rejection: the step throws <see cref="SagaRejectedException" />
    ///     so the generated orchestrator runs the compensation chain and stops without retrying.
    /// </summary>
    /// <remarks>
    ///     Not that <c>Handle</c> itself leaves the saga in <c>Cancelled</c>. It does not,
    ///     by design — returning normally would let the declared <c>NextState</c> advance the saga down the
    ///     happy path and compensation would never fire (see the comment on the step). Reaching a terminal
    ///     state is the orchestrator's job, so a unit test calling the step directly cannot observe it; what
    ///     the step guarantees is the rejection, and that is what is asserted here.
    /// </remarks>
    [Fact]
    public void Handle_IdentityVerified_Invalid_RejectsSoCompensationRuns()
    {
        var saga = new CheckInSaga { State = CheckInState.GuestVerified, ReservationId = Guid.NewGuid() };

        var act = () => saga.Handle(new IdentityVerified(saga.ReservationId, IsValid: false));

        act.Should().Throw<SagaRejectedException>()
            .WithMessage($"*{saga.ReservationId}*");

        saga.State.Should().Be(CheckInState.GuestVerified,
            "the step must not advance the saga itself — the orchestrator owns state transitions");
    }

    [Fact]
    public void Handle_RoomReady_CompletesCheckIn()
    {
        var saga = new CheckInSaga { State = CheckInState.RoomAssigned, ReservationId = Guid.NewGuid() };

        var action = saga.Handle(new RoomReady(saga.ReservationId, "101A"));

        saga.State.Should().Be(CheckInState.Completed);
        saga.AssignedRoom.Should().Be("101A");
        saga.CompletedAt.Should().NotBeNull();
        action.RoomNumber.Should().Be("101A");
    }

    [Fact]
    public void FullFlow_GuestArrival_ToCompletion()
    {
        var saga = new CheckInSaga();
        var reservationId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        // Step 1: Guest arrives
        var verifyAction = saga.Handle(new GuestArrived(reservationId, guestId, DateTimeOffset.UtcNow));
        saga.State.Should().Be(CheckInState.GuestVerified);

        // Step 2: Identity verified
        var assignAction = saga.Handle(new IdentityVerified(reservationId, IsValid: true));
        assignAction.Should().NotBeNull();

        // Simulate state transition (normally done by orchestrator)
        saga.State = CheckInState.RoomAssigned;

        // Step 3: Room ready
        var completeAction = saga.Handle(new RoomReady(reservationId, "305B"));
        saga.State.Should().Be(CheckInState.Completed);
        saga.CompletedAt.Should().NotBeNull();
        saga.AssignedRoom.Should().Be("305B");
    }

    [Fact]
    public void SagaProperties_ImplementISaga()
    {
        var saga = new CheckInSaga
        {
            Id = Guid.NewGuid(),
            State = CheckInState.Pending,
            CorrelationId = "test-corr",
            StartedAt = DateTimeOffset.UtcNow
        };

        saga.Id.Should().NotBeEmpty();
        saga.CorrelationId.Should().Be("test-corr");
        saga.CompletedAt.Should().BeNull();
    }
}
