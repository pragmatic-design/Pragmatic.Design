using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Pragmatic.Jobs.Samples;

/// <summary>
///     Example: batch-style recurring job that processes large datasets.
///     Runs nightly at 3 AM. Can be combined with BatchDispatcher for scatter/gather.
/// </summary>
public record PriceRecalcParams(string[] PropertyIds);

[RecurringJob("0 3 * * *", Id = "nightly-price-recalc")]
[Retry(MaxAttempts = 2, Strategy = BackoffStrategy.Fixed, BaseDelayMs = 5000)]
[Timeout(TimeoutSeconds = 1800)] // 30 min timeout for large datasets
public sealed partial class RecalculatePricesJob : IJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        Console.WriteLine($"[{context.ScheduledAt:yyyy-MM-dd}] Recalculating prices for all properties...");
        // In real code:
        // var properties = await propertyRepo.GetAllAsync(ct);
        // var batchId = await batchDispatcher.DispatchAsync(
        //     new RecalcPricesCommand(properties.Select(p => p.Id).ToArray()),
        //     label: $"Price recalc {context.ScheduledAt:yyyy-MM-dd}");
        return Task.CompletedTask;
    }
}
