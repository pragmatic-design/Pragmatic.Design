using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Entities;

namespace Pragmatic.Messaging.EFCore.Outbox;

/// <summary>
///     Periodically deletes delivered (<c>ProcessedAt != null</c>) outbox rows older than
///     <see cref="MessagingOptions.OutboxRetention"/> so <c>__OutboxMessages</c> does not grow
///     without bound. Mirrors <c>IdempotencyPurgeService</c>: registered once by
///     <see cref="MessagingOutboxExtensions.AddMessagingOutbox{TContext}"/>, it sweeps every
///     registered <see cref="IOutboxSource"/> from a fresh scope per run (each EF source is scoped
///     to its DbContext).
/// </summary>
public sealed partial class OutboxPurgeService(
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingOptions> options,
    ILogger<OutboxPurgeService> logger) : BackgroundService
{
    private readonly MessagingOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.OutboxPurgeInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await PurgeAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using var _ = scope.ConfigureAwait(false);

            foreach (var source in scope.ServiceProvider.GetServices<IOutboxSource>())
                await source.PurgeProcessedAsync(_options.OutboxRetention, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never kill the timer loop: a transient DB failure just skips this cycle.
            LogPurgeFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox purge failed; will retry next cycle")]
    private partial void LogPurgeFailed(Exception ex);
}
