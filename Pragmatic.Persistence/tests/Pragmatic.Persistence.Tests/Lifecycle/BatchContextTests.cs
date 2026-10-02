using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Persistence.Tests.Lifecycle;

public sealed class BatchContextTests
{
    [Fact]
    public void Current_Default_IsNull()
    {
        BatchContext.Current.Should().BeNull();
    }

    [Fact]
    public void Current_WhenActive_ReturnsCurrent()
    {
        using var batch = new BatchContext();
        BatchContext.Current.Should().BeSameAs(batch);
    }

    [Fact]
    public void Dispose_RestoresNull()
    {
        var batch = new BatchContext();
        batch.Dispose();
        BatchContext.Current.Should().BeNull();
    }

    [Fact]
    public void NestedScopes_RestorePrevious()
    {
        using var outer = new BatchContext();
        using (var inner = new BatchContext())
        {
            BatchContext.Current.Should().BeSameAs(inner);
        }

        BatchContext.Current.Should().BeSameAs(outer);
    }

    [Fact]
    public void DeferEvent_AccumulatesEvents()
    {
        using var batch = new BatchContext();

        batch.DeferEvent("event1");
        batch.DeferEvent("event2");

        batch.DeferredEvents.Should().HaveCount(2);
        batch.DeferredEvents.Should().Contain("event1");
    }

    [Fact]
    public void AccumulateEntity_AccumulatesEntities()
    {
        using var batch = new BatchContext();
        var entity = new object();

        batch.AccumulateEntity(entity);

        batch.AccumulatedEntities.Should().ContainSingle().Which.Should().BeSameAs(entity);
    }

    [Fact]
    public void GetEventsByType_GroupsCorrectly()
    {
        using var batch = new BatchContext();

        batch.DeferEvent("string-event-1");
        batch.DeferEvent("string-event-2");
        batch.DeferEvent(42);
        batch.DeferEvent(99);
        batch.DeferEvent(3.14);

        var grouped = batch.GetEventsByType();

        grouped.Should().HaveCount(3);
        grouped[typeof(string)].Should().HaveCount(2);
        grouped[typeof(int)].Should().HaveCount(2);
        grouped[typeof(double)].Should().HaveCount(1);
    }

    [Fact]
    public void Options_DefaultsToEmptyOptions()
    {
        using var batch = new BatchContext();
        batch.Options.ChunkSize.Should().Be(0);
        batch.Options.ChunkAsTransaction.Should().BeFalse();
    }

    [Fact]
    public void Options_CustomOptions()
    {
        var options = new BulkOperationOptions { ChunkSize = 100, ChunkAsTransaction = true };
        using var batch = new BatchContext(options);

        batch.Options.ChunkSize.Should().Be(100);
        batch.Options.ChunkAsTransaction.Should().BeTrue();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var batch = new BatchContext();
        batch.Dispose();
        batch.Dispose(); // should not throw

        BatchContext.Current.Should().BeNull();
    }
}
