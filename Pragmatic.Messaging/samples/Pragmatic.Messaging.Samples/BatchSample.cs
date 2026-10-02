using Pragmatic.Messaging.Batch;

namespace Pragmatic.Messaging.Samples;

// =========================================================================
// Sample: Batch Processing — demonstrates IBatchSplitter + BatchProgress
//
// Use case: recalculate prices for 200K products
// Flow: RecalculatePricesCommand → split into batches of 100 → process each
// =========================================================================

public record RecalculatePricesCommand(Guid[] ProductIds, string Reason);

public record RecalculatePriceBatch(Guid[] ProductIds);

/// <summary>
///     Splits a large recalculation request into batches of 100 products.
/// </summary>
public class PriceRecalculationSplitter : IBatchSplitter<RecalculatePricesCommand, RecalculatePriceBatch>
{
    private const int _batchSize = 100;

    public IReadOnlyList<RecalculatePriceBatch> Split(RecalculatePricesCommand command)
    {
        return command.ProductIds
            .Chunk(_batchSize)
            .Select(chunk => new RecalculatePriceBatch(chunk))
            .ToList();
    }
}

/// <summary>
///     Demonstrates batch progress tracking.
/// </summary>
public static class BatchProgressDemo
{
    public static async Task RunAsync()
    {
        var store = new InMemoryBatchProgressStore();

        // Create batch
        var batchId = Guid.NewGuid();
        var totalItems = 2000; // 200K products / 100 per batch
        await store.CreateAsync(new BatchProgress
        {
            BatchId = batchId,
            Total = totalItems,
            StartedAt = DateTimeOffset.UtcNow,
            Label = "Price recalculation 2026-03-25"
        });

        // Simulate processing
        for (var i = 0; i < totalItems; i++)
        {
            if (i % 50 == 49) // 2% failure rate
                await store.IncrementFailedAsync(batchId);
            else
                await store.IncrementCompletedAsync(batchId);

            // Check progress periodically
            if (i % 500 == 499)
            {
                var progress = await store.GetProgressAsync(batchId);
                Console.WriteLine(
                    $"Batch {batchId:N}: {progress!.ProgressPercent:F1}% " +
                    $"({progress.Completed} ok, {progress.Failed} failed, {progress.Pending} pending)");
            }
        }

        var final = await store.GetProgressAsync(batchId);
        Console.WriteLine($"Batch complete: {final!.Completed} ok, {final.Failed} failed");
    }
}
