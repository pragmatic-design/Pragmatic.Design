using Pragmatic.Testing.Assertions;
using Pragmatic.Messaging.Batch;

namespace Pragmatic.Messaging.Tests.Unit;

public class InMemoryBatchProgressStoreTests
{
    [Fact]
    public async Task CreateAndGet_ShouldRoundTrip()
    {
        var store = new InMemoryBatchProgressStore();
        var batchId = Guid.NewGuid();
        var progress = new BatchProgress
        {
            BatchId = batchId,
            Total = 100,
            StartedAt = DateTimeOffset.UtcNow,
            Label = "Test batch"
        };

        await store.CreateAsync(progress);

        var result = await store.GetProgressAsync(batchId);
        result.Should().NotBeNull();
        result!.Total.Should().Be(100);
        result.Completed.Should().Be(0);
        result.Failed.Should().Be(0);
        result.IsComplete.Should().BeFalse();
        result.Label.Should().Be("Test batch");
    }

    [Fact]
    public async Task IncrementCompleted_ShouldTrackProgress()
    {
        var store = new InMemoryBatchProgressStore();
        var batchId = Guid.NewGuid();
        await store.CreateAsync(new BatchProgress { BatchId = batchId, Total = 3, StartedAt = DateTimeOffset.UtcNow });

        await store.IncrementCompletedAsync(batchId);
        await store.IncrementCompletedAsync(batchId);

        var progress = await store.GetProgressAsync(batchId);
        progress!.Completed.Should().Be(2);
        progress.Pending.Should().Be(1);
        progress.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task IncrementFailed_ShouldTrackFailures()
    {
        var store = new InMemoryBatchProgressStore();
        var batchId = Guid.NewGuid();
        await store.CreateAsync(new BatchProgress { BatchId = batchId, Total = 2, StartedAt = DateTimeOffset.UtcNow });

        await store.IncrementCompletedAsync(batchId);
        await store.IncrementFailedAsync(batchId);

        var progress = await store.GetProgressAsync(batchId);
        progress!.Completed.Should().Be(1);
        progress.Failed.Should().Be(1);
        progress.IsComplete.Should().BeTrue();
        progress.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetActive_ShouldReturnOnlyNonComplete()
    {
        var store = new InMemoryBatchProgressStore();

        await store.CreateAsync(new BatchProgress { BatchId = Guid.NewGuid(), Total = 1, StartedAt = DateTimeOffset.UtcNow });
        var completedId = Guid.NewGuid();
        await store.CreateAsync(new BatchProgress { BatchId = completedId, Total = 1, StartedAt = DateTimeOffset.UtcNow });
        await store.IncrementCompletedAsync(completedId);

        var active = await store.GetActiveAsync();
        active.Should().ContainSingle();
    }

    [Fact]
    public async Task ZeroItemBatch_IsCompleteAndNotActive_OnCreate()
    {
        // A zero-item batch is complete at creation; CreateAsync stamps CompletedAt so it is not
        // "active forever" (BatchStoreParityTests holds the EF store to the same).
        var store = new InMemoryBatchProgressStore();
        var batchId = Guid.NewGuid();
        await store.CreateAsync(new BatchProgress { BatchId = batchId, Total = 0, StartedAt = DateTimeOffset.UtcNow });

        var progress = await store.GetProgressAsync(batchId);
        progress!.IsComplete.Should().BeTrue();
        progress.CompletedAt.Should().NotBeNull();
        (await store.GetActiveAsync()).Should().BeEmpty();
    }

    [Fact]
    public void ProgressPercent_ShouldCalculateCorrectly()
    {
        var progress = new BatchProgress { BatchId = Guid.NewGuid(), Total = 200, Completed = 100, Failed = 50 };
        progress.ProgressPercent.Should().Be(75.0);
    }

    [Fact]
    public void ProgressPercent_WhenZeroTotal_ShouldBeZero()
    {
        var progress = new BatchProgress { BatchId = Guid.NewGuid(), Total = 0 };
        progress.ProgressPercent.Should().Be(0);
    }
}
