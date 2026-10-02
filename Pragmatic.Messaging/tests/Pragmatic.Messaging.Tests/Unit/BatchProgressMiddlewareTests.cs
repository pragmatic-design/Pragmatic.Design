using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Batch;

namespace Pragmatic.Messaging.Tests.Unit;

/// <summary>
///     Batch completion does not depend on the handler remembering to call BatchTracker: the
///     middleware reports each item's real outcome automatically. Left to the handler, one that threw
///     would never increment Failed, so Pending would never reach zero and the batch would stay
///     "active" forever.
/// </summary>
#pragma warning disable CA2007
public class BatchProgressMiddlewareTests
{
    private readonly InMemoryBatchProgressStore _store = new();
    private readonly Guid _batchId = Guid.NewGuid();

    private BatchProgressMiddleware CreateMiddleware()
        => new(_store, NullLogger<BatchProgressMiddleware>.Instance);

    private async Task<MessageContext> SeedBatchAsync(int total = 2)
    {
        await _store.CreateAsync(new BatchProgress
        {
            BatchId = _batchId,
            Total = total,
            StartedAt = DateTimeOffset.UtcNow,
        });

        return MessageContext.New() with
        {
            Headers = new Dictionary<string, string>
            {
                [BatchHeaders.BatchId] = _batchId.ToString(),
                [BatchHeaders.ItemIndex] = "0",
                [BatchHeaders.TotalItems] = total.ToString(),
            },
        };
    }

    [Fact]
    public async Task Redelivery_SameItem_CountedOnce_WithIdempotencyStore()
    {
        // An at-least-once redelivery of the same (batchId, itemIndex) must not inflate the
        // counters (Completed > Total). With an idempotency store the item is counted once.
        var context = await SeedBatchAsync();
        var middleware = new BatchProgressMiddleware(
            _store, NullLogger<BatchProgressMiddleware>.Instance, new InMemoryIdempotencyStore());

        await middleware.InvokeAsync("item", context, () => Task.CompletedTask);
        await middleware.InvokeAsync("item", context, () => Task.CompletedTask); // redelivery, same index

        var progress = await _store.GetProgressAsync(_batchId);
        progress!.Completed.Should().Be(1, "the same batch item counts once even under at-least-once redelivery");
    }

    [Fact]
    public async Task Success_ReportsCompleted_WithoutHandlerCooperation()
    {
        var context = await SeedBatchAsync();

        await CreateMiddleware().InvokeAsync("item", context, () => Task.CompletedTask);

        var progress = await _store.GetProgressAsync(_batchId);
        progress!.Completed.Should().Be(1);
        progress.Failed.Should().Be(0);
    }

    [Fact]
    public async Task UnknownBatchId_DoesNotCountAgainstAnyBatch()
    {
        // A message carrying an unknown/forged batch.id must not create or skew counters —
        // the store is authoritative, so a non-existent batch is ignored.
        await SeedBatchAsync(total: 2); // seeds _batchId in the store
        var unknown = MessageContext.New() with
        {
            Headers = new Dictionary<string, string> { [BatchHeaders.BatchId] = Guid.NewGuid().ToString() },
        };

        await CreateMiddleware().InvokeAsync("item", unknown, () => Task.CompletedTask);

        var progress = await _store.GetProgressAsync(_batchId);
        progress!.Completed.Should().Be(0, "a message for an unknown batch must not touch a real batch");
    }

    [Fact]
    public async Task HandlerThrows_ReportsFailed_AndRethrows()
    {
        var context = await SeedBatchAsync();

        var act = () => CreateMiddleware()
            .InvokeAsync("item", context, () => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>(
            "reporting must never swallow the failure — the pipeline still needs to retry/dead-letter");

        var progress = await _store.GetProgressAsync(_batchId);
        progress!.Failed.Should().Be(1,
            "a throwing handler must still advance the batch, else Pending never reaches zero");
    }

    [Fact]
    public async Task NonBatchMessage_LeavesStoreUntouched()
    {
        await SeedBatchAsync();

        // No batch headers → the middleware is a pass-through.
        await CreateMiddleware().InvokeAsync("item", MessageContext.New(), () => Task.CompletedTask);

        var progress = await _store.GetProgressAsync(_batchId);
        progress!.Completed.Should().Be(0);
        progress.Failed.Should().Be(0);
    }

    [Fact]
    public async Task StoreFailure_DoesNotBreakMessageProcessing()
    {
        var context = await SeedBatchAsync();
        var middleware = new BatchProgressMiddleware(
            new ThrowingStore(), NullLogger<BatchProgressMiddleware>.Instance);

        var act = () => middleware.InvokeAsync("item", context, () => Task.CompletedTask);

        await act.Should().NotThrowAsync(
            "progress bookkeeping must never fail an item that actually succeeded");
    }

    private sealed class ThrowingStore : IBatchProgressStore
    {
        public Task CreateAsync(BatchProgress progress, CancellationToken ct = default) => Task.CompletedTask;
        public Task IncrementCompletedAsync(Guid batchId, CancellationToken ct = default) => throw new InvalidOperationException("store down");
        public Task IncrementFailedAsync(Guid batchId, CancellationToken ct = default) => throw new InvalidOperationException("store down");
        // Report the batch as existing so the middleware proceeds to the (throwing) increment — a null here
        // would make it skip the batch as unknown and the test would pass without exercising the failure path.
        public Task<BatchProgress?> GetProgressAsync(Guid batchId, CancellationToken ct = default)
            => Task.FromResult<BatchProgress?>(new BatchProgress { BatchId = batchId, Total = 1 });
        public Task SetDispatchedCountAsync(Guid batchId, int dispatchedCount, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<BatchProgress>> GetActiveAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BatchProgress>>([]);
    }
}
