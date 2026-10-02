namespace TimeOff.Leave.Events;

/// <summary>
///     HR registered an employee, whose account now waits for them to choose a password.
/// </summary>
public sealed record EmployeeRegistered(
    Guid EmployeeId,
    string WorkEmail,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
