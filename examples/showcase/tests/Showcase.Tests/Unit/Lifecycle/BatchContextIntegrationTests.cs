using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Lifecycle;
using Xunit;

namespace Showcase.Tests.Unit.Lifecycle;

/// <summary>
/// Integration-style tests for BatchContext usage patterns in Showcase scenarios.
/// </summary>
public class BatchContextIntegrationTests
{
    [Fact]
    public void BatchContext_Current_IsNull_ByDefault()
    {
        BatchContext.Current.Should().BeNull();
    }

    [Fact]
    public void BatchContext_Current_IsSet_WhenScoped()
    {
        using var batch = new BatchContext();

        BatchContext.Current.Should().NotBeNull();
    }

    [Fact]
    public void BatchContext_Current_RestoredAfterDispose()
    {
        using (var batch = new BatchContext())
        {
            BatchContext.Current.Should().NotBeNull();
        }

        BatchContext.Current.Should().BeNull();
    }

    [Fact]
    public void BatchContext_DefersEvents()
    {
        using var batch = new BatchContext();

        batch.DeferEvent(new TestDomainEvent("A"));
        batch.DeferEvent(new TestDomainEvent("B"));

        batch.DeferredEvents.Should().HaveCount(2);
    }

    [Fact]
    public void BatchContext_AccumulatesEntities()
    {
        using var batch = new BatchContext();

        batch.AccumulateEntity(new object());
        batch.AccumulateEntity(new object());
        batch.AccumulateEntity(new object());

        batch.AccumulatedEntities.Should().HaveCount(3);
    }

    [Fact]
    public void BatchContext_NestedScope_RestoresParent()
    {
        using var outer = new BatchContext();
        outer.DeferEvent(new TestDomainEvent("outer"));

        using (var inner = new BatchContext())
        {
            inner.DeferEvent(new TestDomainEvent("inner"));
            inner.DeferredEvents.Should().HaveCount(1);
        }

        BatchContext.Current.Should().BeSameAs(outer);
        outer.DeferredEvents.Should().HaveCount(1);
    }

    [Fact]
    public void BatchContext_GetEventsByType_GroupsCorrectly()
    {
        using var batch = new BatchContext();
        batch.DeferEvent(new TestDomainEvent("A"));
        batch.DeferEvent("not a test event");
        batch.DeferEvent(new TestDomainEvent("B"));

        var grouped = batch.GetEventsByType();
        grouped.Should().ContainKey(typeof(TestDomainEvent));
        grouped[typeof(TestDomainEvent)].Should().HaveCount(2);
    }

    private sealed record TestDomainEvent(string Name);
}
