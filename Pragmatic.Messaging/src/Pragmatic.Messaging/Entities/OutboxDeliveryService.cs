using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Diagnostics;
using Pragmatic.MultiTenancy;
using Pragmatic.Telemetry;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Messaging.Entities;

/// <summary>
///     Background service that polls outbox sources and delivers messages via <see cref="IMessageBus"/>.
/// </summary>
public sealed partial class OutboxDeliveryService(
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingOptions> options,
    ILogger<OutboxDeliveryService> logger)
    : BackgroundService
{
    /// <summary>
    ///     Key space of the publish-side claim, so it cannot be mistaken for a delivery.
    /// </summary>
    /// <remarks>
    ///     ⚠️ One <see cref="IIdempotencyStore"/> serves two mechanisms that both key on a message id and
    ///     mean different things by it: this pump claims "this outbox row was published", and
    ///     <c>TransportAwareMessageBus.DispatchAsync</c> claims "this delivery was handled". The two keys
    ///     must differ. A shared key is invisible while a service only handles what <b>another</b>
    ///     service published — the two stores are in two processes. In a service that handles its own
    ///     published message the delivery would come back to the store that published it, find the pump's
    ///     claim from moments earlier and be dropped as a duplicate of itself: no error, the row marked
    ///     processed, the queue empty, the handler never entered.
    /// </remarks>
    private const string PublishClaimPrefix = "outbox-publish:";

    private readonly MessagingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(_options.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeliverBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogDeliveryError(ex);
            }

            // Catch cancellation from the delay so ExecuteAsync exits cleanly without
            // an unhandled OperationCanceledException propagating to the host.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        LogStopped();
    }

    private async Task DeliverBatchAsync(CancellationToken ct)
    {
        // The shared database first: with no ambient tenant the connection interceptor leaves every
        // DbContext where it was registered, which is where tenants without a database of their own
        // write.
        await DrainAsync(ct).ConfigureAwait(false);

        // Then one pass per tenant that has a database of its own, inside that tenant's scope — because
        // an outbox row lives in the same database as the change it announces, and nothing else in this
        // process will ever open that database. ⚠️ Without this pass those rows are never read: a
        // tenant with a dedicated database keeps its rows unprocessed while the shared-schema tenants'
        // messages cross.
        foreach (var tenantId in await TenantsWithADatabaseOfTheirOwnAsync(ct).ConfigureAwait(false))
        {
            using var tenantScope = TenantScope.BeginScope(tenantId);

            await DrainAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     The active tenants whose rows are in a database of their own.
    /// </summary>
    /// <remarks>
    ///     Empty when the application is not multi-tenant, or is multi-tenant on a shared schema: there
    ///     is then one database, and the pass above has already drained it. The cost is one polling query
    ///     per dedicated database per interval, which is the price of delivering their messages at all.
    /// </remarks>
    private async Task<IReadOnlyList<string>> TenantsWithADatabaseOfTheirOwnAsync(CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);

        if (scope.ServiceProvider.GetService<ITenantStore>() is not { } tenants)
            return [];

        var active = await tenants.GetActiveAsync(ct).ConfigureAwait(false);

        return [.. active.Where(t => !string.IsNullOrEmpty(t.ConnectionString)).Select(t => t.TenantId)];
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var sources = scope.ServiceProvider.GetServices<IOutboxSource>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        // One registry per module assembly (SG-generated): try each until one knows the type.
        var registries = scope.ServiceProvider.GetServices<IMessageTypeRegistry>().ToArray();
        var deadLetterStore = scope.ServiceProvider.GetService<IDeadLetterStore>();
        // Optional: only enforce dedup when EnableIdempotency() registered a store.
        var idempotencyStore = scope.ServiceProvider.GetService<IIdempotencyStore>();

        foreach (var source in sources)
        {
            var stopwatch = Stopwatch.StartNew();
            var pending = await source.GetPendingAsync(_options.BatchSize, ct).ConfigureAwait(false);

            if (pending.Count == 0) continue;

            LogProcessingBatch(source.BoundaryName, pending.Count);

            using var activity = MessagingDiagnostics.ActivitySource.StartActivity($"Outbox.Deliver.{source.BoundaryName}");
            activity?.SetTag(MessagingTags.OutboxBoundary, source.BoundaryName);
            activity?.SetTag(MessagingTags.OutboxBatchSize, pending.Count);

            foreach (var outboxMessage in pending)
            {
                // The id this attempt will claim once the publish has succeeded. Null when no store is
                // registered, and it stays unclaimed on every path that does not reach the publish —
                // which is what stops a failed attempt from looking like a delivered one.
                string? claimedMessageId = null;

                try
                {
                    // Resolve message type via SG-generated registries (AOT-safe switch expressions)
                    object? message = null;
                    foreach (var registry in registries)
                    {
                        message = registry.Deserialize(outboxMessage.MessageType, outboxMessage.Payload);
                        if (message is not null) break;
                    }

                    if (message is null)
                    {
                        LogUnknownMessageType(outboxMessage.MessageType);
                        await HandleDeadLetterAsync(source, outboxMessage, deadLetterStore,
                            $"Unknown message type: {outboxMessage.MessageType}", ct).ConfigureAwait(false);
                        continue;
                    }

                    // Reconstruct context
                    var context = new MessageContext(
                        MessageId: outboxMessage.Id.ToString("N"),
                        CorrelationId: outboxMessage.CorrelationId,
                        TenantId: outboxMessage.TenantId,
                        UserId: outboxMessage.UserId,
                        RetryCount: outboxMessage.RetryCount);

                    // Idempotency: at-least-once delivery means a message can be re-published after a
                    // crash between PublishAsync and MarkProcessed. When a store is registered, the
                    // claim says that publish HAPPENED, so an already-claimed row is skipped (marked
                    // processed rather than published a second time).
                    //
                    // ⚠️ Read here, written after the publish — and the order is the whole point. A
                    // claim taken first would mean "somebody is trying" while it is read as "somebody
                    // succeeded": a process that died in between would come back, find the claim and
                    // mark the row processed without ever publishing it. The message would be gone and
                    // the row would say it was delivered. This order trades that loss for a possible
                    // duplicate when the crash lands between the
                    // publish and the claim — which is what at-least-once delivery is, and what the
                    // consumer's own idempotency is there to absorb.
                    //
                    // ⚠️ Under PublishClaimPrefix, and not under the bare message id: see the remark there.
                    if (idempotencyStore is not null)
                    {
                        var publishClaim = PublishClaimPrefix + context.MessageId;
                        if (await idempotencyStore.HasBeenProcessedAsync(publishClaim, ct).ConfigureAwait(false))
                        {
                            LogMessageDelivered(outboxMessage.MessageType, outboxMessage.Id);
                            await source.MarkProcessedAsync(outboxMessage.Id, ct).ConfigureAwait(false);
                            continue;
                        }

                        claimedMessageId = publishClaim;
                    }

                    // Restore the originating tenant for the duration of delivery so any
                    // tenant-scoped EF query inside a handler resolves the correct filter. Without
                    // this the worker's null tenant leaks across tenants.
                    // ⚠️ TenantScope, not IMutableTenantContext.SetTenant. Five workers restored the tenant by
                    // hand, each resolving the mutable context from its own scope, while the mechanism the
                    // documentation points at had zero callers in the framework — because it could not be
                    // called: it lived in Pragmatic.MultiTenancy and these modules only reference
                    // Abstractions. Moving it beside the contract it serves is what made one way possible.
                    // The registered ITenantContext is AmbientTenantContext: it prefers the request tenant
                    // and falls back to this ambient one, and outside a request there is no request tenant.
                    using var tenantScope = outboxMessage.TenantId is { Length: > 0 } messageTenant
                        ? TenantScope.BeginScope(messageTenant)
                        : null;

                    // Publish (not dispatch) so the configured transport delivers
                    // the message — local-only DispatchAsync would silently drop
                    // cross-service events when RabbitMQ/Kafka/Channels are active.
                    await bus.PublishAsync(message, message.GetType(), context, ct).ConfigureAwait(false);

                    // Now, and not before: the claim records a publish that has happened. A failure
                    // from here on leaves it standing, which is correct — the message IS out, and the
                    // next sweep must close the row rather than send it again.
                    if (claimedMessageId is not null && idempotencyStore is not null)
                        await idempotencyStore.TryMarkAsProcessedAsync(claimedMessageId, ct).ConfigureAwait(false);

                    await source.MarkProcessedAsync(outboxMessage.Id, ct).ConfigureAwait(false);

                    LogMessageDelivered(outboxMessage.MessageType, outboxMessage.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // ⚠️ Nothing to release. The claim is written after a successful publish, so a
                    // failure before that point never took one — which is what makes a shutdown safe
                    // too: an OperationCanceledException skips this branch, and no claim is left
                    // standing on a message that was never sent.
                    LogMessageDeliveryFailed(outboxMessage.MessageType, outboxMessage.Id, ex);

                    if (outboxMessage.RetryCount >= _options.MaxRetries)
                    {
                        await HandleDeadLetterAsync(source, outboxMessage, deadLetterStore,
                            ex.Message, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await source.MarkFailedAsync(outboxMessage.Id, ex.Message, ct).ConfigureAwait(false);
                    }
                }
            }

            stopwatch.Stop();
            MessagingDiagnostics.OutboxDeliveryDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("outbox.boundary", source.BoundaryName));

            activity?.SetSuccess();
        }
    }

    private async Task HandleDeadLetterAsync(
        IOutboxSource source,
        OutboxMessage outboxMessage,
        IDeadLetterStore? deadLetterStore,
        string error,
        CancellationToken ct)
    {
        var deadLetter = new DeadLetterMessage(
            MessageType: outboxMessage.MessageType,
            Payload: outboxMessage.Payload,
            Error: error,
            RetryCount: outboxMessage.RetryCount,
            Context: new MessageContext(
                MessageId: outboxMessage.Id.ToString("N"),
                CorrelationId: outboxMessage.CorrelationId,
                TenantId: outboxMessage.TenantId,
                UserId: outboxMessage.UserId,
                RetryCount: outboxMessage.RetryCount),
            FailedAt: DateTimeOffset.UtcNow);

        if (deadLetterStore is not null)
            await deadLetterStore.StoreAsync(deadLetter, ct).ConfigureAwait(false);

        // Mark as processed (dead-lettered) so it's not retried
        await source.MarkProcessedAsync(outboxMessage.Id, ct).ConfigureAwait(false);

        LogMessageDeadLettered(outboxMessage.MessageType, outboxMessage.Id);
    }

    // =========================================================================
    // LoggerMessage - Zero Allocation Logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Outbox delivery service started, polling every {IntervalSeconds}s")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Outbox delivery service stopped")]
    private partial void LogStopped();

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Processing outbox batch for boundary {BoundaryName}: {Count} message(s)")]
    private partial void LogProcessingBatch(string boundaryName, int count);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Delivered outbox message {MessageType} (id: {MessageId})")]
    private partial void LogMessageDelivered(string messageType, Guid messageId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed to deliver outbox message {MessageType} (id: {MessageId})")]
    private partial void LogMessageDeliveryFailed(string messageType, Guid messageId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Unknown message type in outbox: {MessageType}")]
    private partial void LogUnknownMessageType(string messageType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Message dead-lettered: {MessageType} (id: {MessageId})")]
    private partial void LogMessageDeadLettered(string messageType, Guid messageId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Outbox delivery batch error")]
    private partial void LogDeliveryError(Exception ex);
}
