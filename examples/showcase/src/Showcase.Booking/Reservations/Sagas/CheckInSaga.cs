using Pragmatic.Messaging.Attributes;
using Pragmatic.Messaging.Saga;

namespace Showcase.Booking.Reservations.Sagas;

// Events that drive the saga — all correlate on ReservationId so subsequent events
// are routed to the same saga instance by the SG-generated event handlers.
public record GuestArrived(Guid ReservationId, Guid GuestId, DateTimeOffset ArrivedAt) : ICorrelatedMessage
{
    public string CorrelationId => ReservationId.ToString();
}

public record IdentityVerified(Guid ReservationId, bool IsValid) : ICorrelatedMessage
{
    public string CorrelationId => ReservationId.ToString();
}

public record RoomReady(Guid ReservationId, string RoomNumber) : ICorrelatedMessage
{
    public string CorrelationId => ReservationId.ToString();
}

// Actions dispatched by saga steps
public record VerifyGuestIdentityAction(Guid ReservationId, Guid GuestId);
public record AssignRoomAction(Guid ReservationId);
public record CompleteCheckInAction(Guid ReservationId, string RoomNumber);
public record CancelCheckInAction(Guid ReservationId, string Reason);

/// <summary>
///     Orchestrates the check-in process:
///     GuestArrived → verify identity → assign room → complete check-in.
///     Demonstrates [Saga], [SagaStart], [InState], [CompensateWith].
/// </summary>
[Saga<CheckInState>]
public partial class CheckInSaga : ISaga<CheckInState>
{
    public Guid Id { get; set; }
    public CheckInState State { get; set; }
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    // Saga-specific data
    public Guid ReservationId { get; set; }
    public string? AssignedRoom { get; set; }

    /// <summary>Guest arrives → dispatch identity verification.</summary>
    [SagaStart]
    public VerifyGuestIdentityAction Handle(GuestArrived @event)
    {
        ReservationId = @event.ReservationId;
        State = CheckInState.GuestVerified;
        return new VerifyGuestIdentityAction(@event.ReservationId, @event.GuestId);
    }

    /// <summary>
    ///     Identity verified → assign room. Compensate with cancellation on failure.
    ///     Also opens a 10-minute window to receive <c>RoomReady</c>; if the window
    ///     expires the saga-timeout runner walks the compensation chain and marks
    ///     the instance <c>TimedOut</c>.
    /// </summary>
    [InState(CheckInState.GuestVerified, NextState = CheckInState.RoomAssigned)]
    [CompensateWith<CancelCheckInAction>]
    [SagaTimeout(Duration = "00:10:00")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Saga step methods are invoked on the saga instance by the generated orchestrator; they cannot be static.")]
    public AssignRoomAction Handle(IdentityVerified @event)
    {
        // Business rejection: throw SagaRejectedException so the orchestrator runs the compensation
        // chain (publishes CancelCheckInAction), marks the saga Compensated, and stops — without
        // retrying. Returning here instead would let the declared NextState (RoomAssigned) advance the
        // saga down the happy path and would never fire compensation.
        if (!@event.IsValid)
            throw new SagaRejectedException($"Identity verification failed for reservation {@event.ReservationId}");

        return new AssignRoomAction(@event.ReservationId);
    }

    /// <summary>Room assigned → complete check-in.</summary>
    [InState(CheckInState.RoomAssigned, NextState = CheckInState.Completed)]
    public CompleteCheckInAction Handle(RoomReady @event)
    {
        AssignedRoom = @event.RoomNumber;
        State = CheckInState.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        return new CompleteCheckInAction(@event.ReservationId, @event.RoomNumber);
    }
}
