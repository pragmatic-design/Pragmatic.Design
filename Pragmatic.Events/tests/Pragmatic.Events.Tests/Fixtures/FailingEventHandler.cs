namespace Pragmatic.Events.Tests.Fixtures;

/// <summary>
///     A handler that always throws, for testing fail-fast behavior.
/// </summary>
public sealed class FailingEventHandler : IDomainEventHandler<TestDomainEvent>
{
    public Task HandleAsync(TestDomainEvent @event, CancellationToken ct = default)
    {
        throw new InvalidOperationException("Handler failed on purpose.");
    }
}
