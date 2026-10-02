using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.MultiTenancy;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications.Delivery;

/// <summary>
///     Background service that consumes enqueued notifications from the BoundedChannel and delivers them via the pipeline.
/// </summary>
internal sealed partial class NotificationDeliveryWorker(
    NotificationDeliveryChannel deliveryChannel,
    IServiceScopeFactory scopeFactory,
    INotificationStore store,
    ILogger<NotificationDeliveryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogWorkerStarted();

        try
        {
            await foreach (var queued in deliveryChannel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
                await DeliverAsync(queued, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down — fall through to drain whatever is already buffered.
        }

        await DrainBufferedAsync().ConfigureAwait(false);

        LogWorkerStopped();
    }

    private async Task DeliverAsync(QueuedNotification queued, CancellationToken ct)
    {
        try
        {
            // One scope per notification: the pipeline and its resolver/preference provider are scoped
            // so they can reach a DbContext, and each delivery must get a fresh one — the worker itself
            // is a singleton and lives for the whole process.
            using var scope = scopeFactory.CreateScope();

            // Restore the tenant the notification was enqueued under, so tenant-scoped queries made
            // while resolving recipients see the right data.
            // ⚠️ TenantScope, not IMutableTenantContext.SetTenant. Five workers restored the tenant by
            // hand, each resolving the mutable context from its own scope, while the mechanism the
            // documentation points at had zero callers in the framework — because it could not be
            // called: it lived in Pragmatic.MultiTenancy and these modules only reference
            // Abstractions. Moving it beside the contract it serves is what made one way possible.
            // The registered ITenantContext is AmbientTenantContext: it prefers the request tenant
            // and falls back to this ambient one, and outside a request there is no request tenant.
            using var tenantScope = queued.TenantId is { Length: > 0 } queuedTenant
                ? TenantScope.BeginScope(queuedTenant)
                : null;

            var pipeline = scope.ServiceProvider.GetRequiredService<NotificationPipeline>();

            var result = await pipeline.ExecuteAsync(queued.Request, queued.TrackingId, ct).ConfigureAwait(false);
            if (!result.Success)
                LogDeliveryFailed(result.Errors);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogDeliveryException(ex);

            // The pipeline settles the tracking record on its own failure paths; an exception
            // escaping it (resolver/store faults) would otherwise leave the record Pending forever.
            try
            {
                await store.UpdateStatusAsync(queued.TrackingId, DeliveryStatus.Failed, ex.Message, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception updateEx)
            {
                LogTrackingUpdateFailed(queued.TrackingId, updateEx);
            }
        }
    }

    /// <summary>
    ///     Best-effort delivery of notifications still buffered in the channel when the
    ///     host stops, so a graceful shutdown does not silently drop queued notifications.
    ///     Bounded by a short grace period — the host shutdown timeout still applies.
    /// </summary>
    private async Task DrainBufferedAsync()
    {
        if (!deliveryChannel.Reader.TryRead(out var queued))
            return;

        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        LogDraining();

        do
        {
            if (grace.IsCancellationRequested)
            {
                LogDrainTimeout();
                return;
            }

            try
            {
                await DeliverAsync(queued, grace.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (grace.IsCancellationRequested)
            {
                // Grace period expired mid-delivery: stop draining cleanly instead of letting the
                // OCE escape ExecuteAsync (which would skip the stopped log and fault the service).
                LogDrainTimeout();
                return;
            }
        }
        while (deliveryChannel.Reader.TryRead(out queued));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification delivery worker started")]
    private partial void LogWorkerStarted();

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification delivery worker stopped")]
    private partial void LogWorkerStopped();

    [LoggerMessage(Level = LogLevel.Information, Message = "Draining buffered notifications before shutdown")]
    private partial void LogDraining();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification drain timed out — remaining buffered notifications were not delivered")]
    private partial void LogDrainTimeout();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Background notification delivery failed: {Errors}")]
    private partial void LogDeliveryFailed(IReadOnlyList<string>? errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "Background notification delivery exception")]
    private partial void LogDeliveryException(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mark tracking record {TrackingId} as Failed")]
    private partial void LogTrackingUpdateFailed(Guid trackingId, Exception ex);
}
