using Pragmatic.Testing.Assertions;
using Pragmatic.Messaging.Batch;

namespace Pragmatic.Messaging.Tests.Unit;

public class BatchTrackerTests
{
    private static MessageContext CreateContextWithBatchHeaders(
        Guid batchId,
        int itemIndex = 0,
        int totalItems = 10)
        => MessageContext.New() with
        {
            Headers = new Dictionary<string, string>
            {
                [BatchHeaders.BatchId] = batchId.ToString(),
                [BatchHeaders.ItemIndex] = itemIndex.ToString(),
                [BatchHeaders.TotalItems] = totalItems.ToString(),
            }
        };

    [Fact]
    public void TryGetBatchId_ReturnsId_WhenHeaderPresent()
    {
        var expectedId = Guid.NewGuid();
        var context = CreateContextWithBatchHeaders(expectedId);

        var result = BatchTracker.TryGetBatchId(context, out var batchId);

        result.Should().BeTrue();
        batchId.Should().Be(expectedId);
    }

    [Fact]
    public void TryGetBatchId_ReturnsNull_WhenNoHeader()
    {
        var context = MessageContext.New();

        var result = BatchTracker.TryGetBatchId(context, out var batchId);

        result.Should().BeFalse();
        batchId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void GetBatchContext_ReturnsFullContext()
    {
        var expectedBatchId = Guid.NewGuid();
        var context = CreateContextWithBatchHeaders(expectedBatchId, itemIndex: 5, totalItems: 20);

        var batchContext = BatchTracker.GetBatchContext(context);

        batchContext.Should().NotBeNull();
        batchContext!.BatchId.Should().Be(expectedBatchId);
        batchContext.ItemIndex.Should().Be(5);
        batchContext.TotalItems.Should().Be(20);
    }

    [Fact]
    public void GetBatchContext_ReturnsNull_WhenNoBatchHeaders()
    {
        var context = MessageContext.New();

        var batchContext = BatchTracker.GetBatchContext(context);

        batchContext.Should().BeNull();
    }

    [Fact]
    public async Task ReportSuccessAsync_IncrementsCompleted()
    {
        var store = new InMemoryBatchProgressStore();
        var batchId = Guid.NewGuid();
        await store.CreateAsync(new BatchProgress
        {
            BatchId = batchId,
            Total = 5,
            StartedAt = DateTimeOffset.UtcNow,
        });

        var context = CreateContextWithBatchHeaders(batchId);
        await BatchTracker.ReportSuccessAsync(context, store);

        var progress = await store.GetProgressAsync(batchId);
        progress!.Completed.Should().Be(1);
        progress.Failed.Should().Be(0);
    }

    [Fact]
    public async Task ReportFailureAsync_IncrementsFailed()
    {
        var store = new InMemoryBatchProgressStore();
        var batchId = Guid.NewGuid();
        await store.CreateAsync(new BatchProgress
        {
            BatchId = batchId,
            Total = 5,
            StartedAt = DateTimeOffset.UtcNow,
        });

        var context = CreateContextWithBatchHeaders(batchId);
        await BatchTracker.ReportFailureAsync(context, store);

        var progress = await store.GetProgressAsync(batchId);
        progress!.Completed.Should().Be(0);
        progress.Failed.Should().Be(1);
    }

    [Fact]
    public async Task ReportSuccessAsync_NoBatchHeader_DoesNotThrow()
    {
        var store = new InMemoryBatchProgressStore();
        var context = MessageContext.New();

        // Should be a no-op when no batch headers present
        var act = () => BatchTracker.ReportSuccessAsync(context, store);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReportFailureAsync_NoBatchHeader_DoesNotThrow()
    {
        var store = new InMemoryBatchProgressStore();
        var context = MessageContext.New();

        var act = () => BatchTracker.ReportFailureAsync(context, store);
        await act.Should().NotThrowAsync();
    }
}
