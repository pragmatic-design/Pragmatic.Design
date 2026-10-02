namespace Pragmatic.Events.Tests.Fixtures;

/// <summary>
///     Entity that uses TimeProvider for deterministic event timestamps.
///     Demonstrates the recommended pattern for testable domain events.
/// </summary>
internal sealed class TimeProviderAwareEntity(TimeProvider? timeProvider = null) : DomainEventSource
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public void DoSomething(string message)
    {
        RaiseEvent(new TestDomainEvent(message) { OccurredAt = _timeProvider.GetUtcNow() });
    }
}
