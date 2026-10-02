using Pragmatic.Testing.Assertions;
using Pragmatic.Events.Tests.Fixtures;

namespace Pragmatic.Events.Tests;

/// <summary>
///     Tests the <c>EventId</c> identity contract: <see cref="DomainEvent"/> assigns a fresh unique
///     GUID per instance (overridable via initializer), while a bare <see cref="IDomainEvent"/> that
///     does not declare one falls back to the <see cref="Guid.Empty"/> default-interface-method.
/// </summary>
public class DomainEventIdentityTests
{
    private sealed record SampleEvent(DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

    [Fact]
    public void DomainEvent_AssignsNonEmptyEventId()
    {
        var evt = new SampleEvent(DateTimeOffset.UtcNow);

        evt.EventId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void DomainEvent_EachInstance_HasUniqueEventId()
    {
        var a = new SampleEvent(DateTimeOffset.UtcNow);
        var b = new SampleEvent(DateTimeOffset.UtcNow);

        a.EventId.Should().NotBe(b.EventId);
    }

    [Fact]
    public void DomainEvent_ExplicitEventId_IsPreserved()
    {
        var id = Guid.NewGuid();

        var evt = new SampleEvent(DateTimeOffset.UtcNow) { EventId = id };

        evt.EventId.Should().Be(id);
    }

    [Fact]
    public void DomainEvent_PreservesOccurredAt()
    {
        var when = DateTimeOffset.UtcNow.AddDays(-1);

        var evt = new SampleEvent(when);

        evt.OccurredAt.Should().Be(when);
    }

    [Fact]
    public void BareDomainEvent_WithoutDeclaredEventId_DefaultsToEmpty()
    {
        // TestDomainEvent implements IDomainEvent directly without declaring EventId — it must
        // resolve to the backward-compatible Guid.Empty default-interface-method.
        IDomainEvent evt = new TestDomainEvent("hello");

        evt.EventId.Should().Be(Guid.Empty);
    }
}
