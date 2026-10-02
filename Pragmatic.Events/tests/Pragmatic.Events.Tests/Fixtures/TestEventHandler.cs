namespace Pragmatic.Events.Tests.Fixtures;

/// <summary>
///     A handler that records which events it processed.
/// </summary>
public sealed class TestEventHandler : IDomainEventHandler<TestDomainEvent>
{
    public List<TestDomainEvent> HandledEvents { get; } = [];

    public Task HandleAsync(TestDomainEvent @event, CancellationToken ct = default)
    {
        HandledEvents.Add(@event);
        return Task.CompletedTask;
    }
}
