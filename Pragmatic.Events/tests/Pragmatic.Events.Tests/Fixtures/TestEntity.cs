namespace Pragmatic.Events.Tests.Fixtures;

/// <summary>
///     Concrete entity for testing DomainEventSource.
/// </summary>
public sealed class TestEntity : DomainEventSource
{
    public void DoSomething(string message)
    {
        RaiseEvent(new TestDomainEvent(message));
    }

    public void DoMultipleThings(params string[] messages)
    {
        var events = messages.Select(m => new TestDomainEvent(m));
        RaiseEvents(events);
    }

    /// <summary>
    ///     Exposes RaiseEvent for testing null guard.
    /// </summary>
    public void RaiseNullEvent() => RaiseEvent(null!);

    /// <summary>
    ///     Exposes RaiseEvents for testing null guard.
    /// </summary>
    public void RaiseNullEvents() => RaiseEvents(null!);
}
