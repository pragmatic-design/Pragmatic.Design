namespace Pragmatic.Events.Tests.Fixtures;

/// <summary>
///     Simple domain event for testing.
/// </summary>
public sealed record TestDomainEvent(string Message) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
