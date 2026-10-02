using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Batch;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

/// <summary>
///     A handler can say its batch item failed without throwing: the item was processed, part
///     of its work was refused, and a throw would retry it, dead-letter it, and roll back what it did.
/// </summary>
/// <remarks>
///     Before, the middleware counted an item that returned as Completed, whatever the handler knew, and
///     <see cref="BatchTracker.ReportFailureAsync" /> would count it a second time on top.
/// </remarks>
#pragma warning disable CA2007
public sealed class ABatchItemItsHandlerFailedTests
{
    private readonly InMemoryBatchProgressStore _store = new();
    private readonly Guid _batchId = Guid.NewGuid();

    [Fact]
    public async Task TheHandlerMarksItFailed_ItCountsAsFailed_Once_AndNothingIsThrown()
    {
        var outcome = new BatchItemOutcome();
        var middleware = new BatchProgressMiddleware(
            _store, NullLogger<BatchProgressMiddleware>.Instance, idempotencyStore: null, outcome: outcome);

        await middleware.InvokeAsync("part", await SeedBatchAsync(), () =>
        {
            outcome.Fail();
            return Task.CompletedTask;
        });

        var progress = (await _store.GetProgressAsync(_batchId))!;
        (progress.Completed, progress.Failed).Should().Be((0, 1));
    }

    /// <summary>The control: a handler that marks nothing completes its item.</summary>
    [Fact]
    public async Task TheHandlerMarksNothing_ItCountsAsCompleted()
    {
        var middleware = new BatchProgressMiddleware(
            _store, NullLogger<BatchProgressMiddleware>.Instance, idempotencyStore: null, outcome: new BatchItemOutcome());

        await middleware.InvokeAsync("part", await SeedBatchAsync(), () => Task.CompletedTask);

        var progress = (await _store.GetProgressAsync(_batchId))!;
        (progress.Completed, progress.Failed).Should().Be((1, 0));
    }

    private async Task<MessageContext> SeedBatchAsync()
    {
        await _store.CreateAsync(new BatchProgress { BatchId = _batchId, Total = 1, StartedAt = DateTimeOffset.UtcNow });
        return MessageContext.New() with
        {
            Headers = new Dictionary<string, string>
            {
                [BatchHeaders.BatchId] = _batchId.ToString(),
                [BatchHeaders.ItemIndex] = "0",
                [BatchHeaders.TotalItems] = "1",
            },
        };
    }
}
