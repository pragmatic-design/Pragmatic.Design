using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Configuration;

namespace Pragmatic.Messaging;

/// <summary>
///     Periodically purges expired entries from the <see cref="IIdempotencyStore"/> so the
///     dedup table does not grow without bound. Registered by
///     <c>MessagingBuilder.EnableIdempotency()</c>; the store is resolved from a fresh scope
///     per run (the EF Core store is scoped to its DbContext).
/// </summary>
public sealed partial class IdempotencyPurgeService(
    IServiceScopeFactory scopeFactory,
    IdempotencyOptions options,
    ILogger<IdempotencyPurgeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PurgeInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await PurgeAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetService<IIdempotencyStore>();
            if (store is null) return;

            await store.PurgeOlderThanAsync(options.Retention, ct).ConfigureAwait(false);
            LogPurged(options.Retention);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never kill the timer loop: a transient store failure just skips this cycle.
            LogPurgeFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Idempotency store purged (retention: {Retention})")]
    private partial void LogPurged(TimeSpan retention);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency store purge failed; will retry next cycle")]
    private partial void LogPurgeFailed(Exception ex);
}
