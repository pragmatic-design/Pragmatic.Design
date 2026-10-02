namespace TimeOff.Leave.Events;

/// <summary>
///     A manager approved or rejected a leave request.
/// </summary>
/// <remarks>
///     Raised by the state machine on the transition itself, whatever operation takes it: the decision
///     is the fact, the endpoint is one way to reach it.
/// </remarks>
public sealed record LeaveRequestDecided(
    Guid LeaveRequestId,
    Guid EmployeeId,
    LeaveRequestStatus Status,
    Guid? DecidedById,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
