using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.Kafka;

/// <summary>
///     Background service that connects <see cref="KafkaTransport"/> and binds every registered
///     <see cref="MessageSubscription"/>. Without this the transport stays <c>Disconnected</c> and every
///     publish/subscribe throws.
/// </summary>
public sealed partial class KafkaConsumerService(
    KafkaTransport transport,
    IServiceScopeFactory scopeFactory,
    IMessageRouter router,
    IEnumerable<MessageSubscription> subscriptions,
    IEnumerable<Pragmatic.Messaging.RequestReply.RequestSubscription> requestSubscriptions,
    ILogger<KafkaConsumerService> logger,
    KillSwitchOptions? killSwitch = null,
    IOptions<MessagingOptions>? messaging = null)
    : BackgroundService
{
    private readonly List<IAsyncDisposable> _subscriptions = [];

    /// <summary>Connects the transport before the host reports started, then starts the binding loop.</summary>
    /// <remarks>
    ///     On .NET 10 a BackgroundService runs all of ExecuteAsync in the background, so a connect made there
    ///     would not have happened when the host finishes starting: a publish issued right away would find the
    ///     transport Disconnected. Connecting here builds the producer and opens no connection, so startup
    ///     still does not wait on the broker.
    /// </remarks>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted();

        _subscriptions.AddRange(
            await TransportSubscriptionBinder.BindAsync(transport, router, scopeFactory, subscriptions, killSwitch, logger, busName: null, messaging?.Value.SubscriberName, stoppingToken)
                .ConfigureAwait(false));

        // Distributed request/reply: bind the SG-generated request executors (responder side).
        _subscriptions.AddRange(
            await Pragmatic.Messaging.RequestReply.RequestReplyBinder.BindAsync(transport, scopeFactory, requestSubscriptions, logger, stoppingToken)
                .ConfigureAwait(false));

        if (_subscriptions.Count == 0)
            LogNoSubscriptions();
        else
            LogSubscribed(_subscriptions.Count);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }

        await transport.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        LogStopped();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        foreach (var sub in _subscriptions)
            await sub.DisposeAsync().ConfigureAwait(false);

        _subscriptions.Clear();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Kafka consumer service started")]
    private partial void LogStarted();

    [LoggerMessage(Level = LogLevel.Information, Message = "Kafka consumer service stopped")]
    private partial void LogStopped();

    [LoggerMessage(Level = LogLevel.Information, Message = "Kafka consumer subscribed to {Count} message type(s)")]
    private partial void LogSubscribed(int count);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Kafka transport is running with zero subscriptions: published messages will not be consumed")]
    private partial void LogNoSubscriptions();
}
