using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.Channels;

/// <summary>
///     Background service that connects <see cref="ChannelTransport"/> and binds every registered
///     <see cref="MessageSubscription"/> to the local dispatch pipeline, then keeps the transport
///     alive until shutdown.
/// </summary>
public sealed partial class ChannelConsumerService(
    ChannelTransport transport,
    IServiceScopeFactory scopeFactory,
    IMessageRouter router,
    IEnumerable<MessageSubscription> subscriptions,
    IEnumerable<Pragmatic.Messaging.RequestReply.RequestSubscription> requestSubscriptions,
    ILogger<ChannelConsumerService> logger,
    KillSwitchOptions? killSwitch = null,
    IOptions<MessagingOptions>? messaging = null)
    : BackgroundService
{
    private readonly List<IAsyncDisposable> _subscriptions = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await transport.ConnectAsync(stoppingToken).ConfigureAwait(false);
        LogStarted();

        // Bind every registered subscription so published messages are actually consumed. Without this
        // the channel fills (BoundedChannelFullMode.Wait) and publishers block forever.
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

        await transport.DisconnectAsync().ConfigureAwait(false);
        LogStopped();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop ExecuteAsync first (sends cancellation and waits for it to exit),
        // then dispose subscriptions so no handler callback fires after disposal.
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        foreach (var sub in _subscriptions)
            await sub.DisposeAsync().ConfigureAwait(false);

        _subscriptions.Clear();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Channel consumer service started")]
    private partial void LogStarted();

    [LoggerMessage(Level = LogLevel.Information, Message = "Channel consumer service stopped")]
    private partial void LogStopped();

    [LoggerMessage(Level = LogLevel.Information, Message = "Channel consumer subscribed to {Count} message type(s)")]
    private partial void LogSubscribed(int count);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Channel transport is running with zero subscriptions: published messages will not be consumed")]
    private partial void LogNoSubscriptions();
}
