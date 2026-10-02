namespace Pragmatic.Events.Tests.Fixtures;

/// <summary>
///     Handler for AnotherTestEvent, used in assembly scanning tests.
/// </summary>
public sealed class AnotherTestEventHandler : IDomainEventHandler<AnotherTestEvent>
{
    public List<AnotherTestEvent> HandledEvents { get; } = [];

    public Task HandleAsync(AnotherTestEvent @event, CancellationToken ct = default)
    {
        HandledEvents.Add(@event);
        return Task.CompletedTask;
    }
}
