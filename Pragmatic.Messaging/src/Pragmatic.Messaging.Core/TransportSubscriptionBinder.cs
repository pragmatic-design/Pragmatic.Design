using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Routing;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Messaging;

/// <summary>
///     Shared logic that binds a transport's subscriptions to the local dispatch pipeline.
///     Used by every transport consumer service (Channels/RabbitMQ/Kafka) so the wiring lives in
///     one place: for each registered <see cref="MessageSubscription"/>, resolve its topic via the
///     same <see cref="IMessageRouter"/> the publisher uses, subscribe, and on each delivery
///     deserialize and dispatch to local handlers within a fresh DI scope. When a
///     <see cref="KillSwitchOptions"/> is provided, consecutive dispatch failures on a
///     subscription trip a per-subscription cool-down (half-open on expiry).
/// </summary>
public static partial class TransportSubscriptionBinder
{
    /// <summary>
    ///     Subscribes the transport to every distinct message type in <paramref name="subscriptions"/>.
    ///     Returns the subscription handles so the caller can dispose them on shutdown.
    /// </summary>
    public static async Task<IReadOnlyList<IAsyncDisposable>> BindAsync(
        IMessageTransport transport,
        IMessageRouter router,
        IServiceScopeFactory scopeFactory,
        IEnumerable<MessageSubscription> subscriptions,
        KillSwitchOptions? killSwitch = null,
        ILogger? logger = null,
        string? busName = null,
        string? subscriberOverride = null,
        CancellationToken ct = default)
    {
        var handles = new List<IAsyncDisposable>();
        var bound = new HashSet<Type>();

        foreach (var subscription in subscriptions)
        {
            // Each consumer service binds only its own bus's subscriptions (null = default bus).
            if (!string.Equals(subscription.BusName, busName, StringComparison.Ordinal))
                continue;

            var messageType = subscription.MessageType;
            if (!bound.Add(messageType))
                continue;

            var topic = router.GetTopic(messageType);

            // ⚠️ The name says WHO is listening, and on most brokers it is the queue name. A name like
            // $"{transport.Name}-{messageType.Name}" — the transport and the message, nothing of the
            // subscriber — would make two services subscribing to one event declare one queue on one
            // broker and divide the messages between them instead of each getting a copy.
            // What the deployment pinned wins over what the module registered; there is no third
            // option, because MessageSubscription refuses to exist without a subscriber.
            var subscriptionName = SubscriptionName.For(
                string.IsNullOrWhiteSpace(subscriberOverride) ? subscription.Subscriber : subscriberOverride!,
                messageType,
                subscription.BusName);
            // Per-subscription failure streak for the kill switch (closure state).
            var consecutiveFailures = 0;

            var handle = await transport.SubscribeAsync(topic, subscriptionName, async (payload, context, innerCt) =>
            {
                // Kill switch: once tripped, pause before attempting the next message
                // (half-open — the attempt below closes the switch on success).
                if (killSwitch is not null && Volatile.Read(ref consecutiveFailures) >= killSwitch.ActivationThreshold)
                {
                    if (logger is not null)
                        LogKillSwitchTripped(logger, subscriptionName, consecutiveFailures, killSwitch.TripDuration);
                    await Task.Delay(killSwitch.TripDuration, innerCt).ConfigureAwait(false);
                }

                // ⚠️ A topic is a boundary, not a type: every subscription of a publishing boundary is
                // delivered every message of it, and deserializing one into the type this subscription
                // was created for yields an object of the right shape and the wrong content — no error,
                // no dead letter, a handler running on defaults. When the publisher said what
                // it published, anything else is not ours: it is another subscription's, already on its
                // way there, so this one acknowledges and returns rather than failing a message that is
                // being handled correctly elsewhere.
                //
                // ⚠️⚠️ "Already on its way there" is the assumption, and it holds only where a topic
                // FANS OUT — each subscription with its own copy. On a transport whose subscriptions
                // share one buffer they compete instead, this one has already taken the message off it,
                // and returning throws it away: a handler runs for some messages and not others, with
                // nothing logged anywhere. A transport that does not fan out a topic cannot be used with
                // this filter.
                if (context.MessageType is { Length: > 0 } published
                    && !string.Equals(published, messageType.FullName, StringComparison.Ordinal))
                {
                    if (logger is not null)
                        LogNotForThisSubscription(logger, subscriptionName, published);

                    return;
                }

                try
                {
                    var scope = scopeFactory.CreateAsyncScope();
                    await using (scope.ConfigureAwait(false))
                    {
                        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                        var serializer = scope.ServiceProvider.GetRequiredService<IMessageSerializer>();

                        // Restore the originating tenant into this fresh consume scope: background delivery
                        // has no ambient tenant, so without this the tenant interceptor would not stamp writes
                        // and the fail-closed tenant query filter would match no rows (saga/EF handlers).
                        // Runs before claim-check retrieve so the claim-check store is tenant-scoped too.
                        using var tenantRestore = context.TenantId is { Length: > 0 } originatingTenant
                            ? TenantScope.BeginScope(originatingTenant)
                            : null;

                        // Claim check: a checked message carries only the reference — retrieve the real
                        // payload and deserialize it STREAMING (no full buffer). Deletion happens only AFTER
                        // a successful dispatch so redeliveries can re-read the blob.
                        object? message;
                        string? claimCheckReference = null;
                        if (context.Headers is not null &&
                            context.Headers.TryGetValue(IClaimCheckStore.HeaderName, out claimCheckReference) &&
                            claimCheckReference is not null)
                        {
                            var claimCheckStore = scope.ServiceProvider.GetRequiredService<IClaimCheckStore>();
                            Stream claimStream;
                            try
                            {
                                claimStream = await claimCheckStore.RetrieveAsync(claimCheckReference, innerCt).ConfigureAwait(false);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                // A missing claim-check blob means the message was already fully consumed
                                // (the blob is only deleted AFTER a successful dispatch, when DeleteAfterConsume
                                // is on). Treat this redelivered duplicate as processed — drop it (ack) instead
                                // of rethrowing into a nack→redeliver→poison loop that idempotency cannot break
                                // (retrieval precedes dedup). Fan-out safety is handled by DeleteAfterConsume=false.
                                if (logger is not null)
                                    LogClaimCheckMissing(logger, subscriptionName, claimCheckReference, ex.Message);
                                Volatile.Write(ref consecutiveFailures, 0);
                                return;
                            }

                            await using (claimStream.ConfigureAwait(false))
                                message = await serializer.DeserializeAsync(claimStream, messageType, innerCt).ConfigureAwait(false);
                        }
                        else
                        {
                            message = serializer.Deserialize(payload, messageType);
                        }

                        if (message is not null)
                        {
                            // Stamp the bus so the local dispatcher's [OnBus] filtering targets
                            // exactly this bus's handlers.
                            var dispatchContext = busName is null ? context : context with { BusName = busName };
                            await bus.DispatchAsync(message, dispatchContext, innerCt).ConfigureAwait(false);
                        }

                        if (claimCheckReference is not null &&
                            scope.ServiceProvider.GetService<ClaimCheckOptions>()?.DeleteAfterConsume == true)
                        {
                            var claimCheckStore = scope.ServiceProvider.GetRequiredService<IClaimCheckStore>();
                            // Delete with None: the dispatch already succeeded, so a cancellation here must not
                            // propagate out of the callback (nack → redeliver → the handler re-runs the message).
                            await claimCheckStore.DeleteAsync(claimCheckReference, CancellationToken.None).ConfigureAwait(false);
                        }
                    }

                    Volatile.Write(ref consecutiveFailures, 0);
                }
                catch
                {
                    Interlocked.Increment(ref consecutiveFailures);
                    throw;
                }
            }, ct).ConfigureAwait(false);

            handles.Add(handle);
        }

        return handles;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Kill switch tripped for subscription {Subscription} after {Failures} consecutive failures; pausing consumption for {Pause}")]
    private static partial void LogKillSwitchTripped(ILogger logger, string subscription, int failures, TimeSpan pause);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Claim-check blob {Reference} missing for {Subscription} — treating as already-consumed duplicate and dropping ({Error})")]
    private static partial void LogClaimCheckMissing(ILogger logger, string subscription, string reference, string error);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "{Subscription} skipped a {PublishedType}: the topic carries every message of its boundary, and this one belongs to another subscription")]
    private static partial void LogNotForThisSubscription(ILogger logger, string subscription, string publishedType);
}
