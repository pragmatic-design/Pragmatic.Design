namespace Pragmatic.Events.Tests.Fixtures;

/// <summary>
///     A second event type for testing multi-event dispatch scenarios.
/// </summary>
public sealed record AnotherTestEvent(int Value) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
