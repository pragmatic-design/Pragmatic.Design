namespace Pragmatic.Events.Tests.Fixtures;

/// <summary>
///     A DB entity that raises domain events, for testing the interceptor.
/// </summary>
public sealed class TestDbEntity : DomainEventSource
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    public void ChangeName(string newName)
    {
        Name = newName;
        RaiseEvent(new TestDomainEvent($"Name changed to {newName}"));
    }
}
