using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.RabbitMQ;

/// <summary>
///     Background service that connects <see cref="RabbitMqTransport"/> and binds every registered
///     <see cref="MessageSubscription"/>. Without this the transport stays <c>Disconnected</c> and every
///     publish/subscribe throws.
/// </summary>
public sealed partial class RabbitMqConsumerService(
    RabbitMqTransport transport,
    IServiceScopeFactory scopeFactory,
    IMessageRouter router,
    IEnumerable<MessageSubscription> subscriptions,
    IEnumerable<Pragmatic.Messaging.RequestReply.RequestSubscription> requestSubscriptions,
    ILogger<RabbitMqConsumerService> logger,
    KillSwitchOptions? killSwitch = null,
    IOptions<MessagingOptions>? messaging = null,
    RabbitMqOptions? options = null)
    : BackgroundService
{
    private readonly List<IAsyncDisposable> _subscriptions = [];
    private readonly CancellationTokenSource _stopping = new();
    private Task _connecting = Task.CompletedTask;

    /// <summary>Begins the connect before the host reports started, then starts the binding loop.</summary>
    /// <remarks>
    ///     On .NET 10 a BackgroundService runs all of ExecuteAsync in the background, so a connect begun there
    ///     would not have begun when the host finishes starting, and a publish issued right away would find the
    ///     transport Disconnected. Begun here, the transport is Connecting by then, and the publish waits for
    ///     it. Not awaited: the connect opens a connection to the broker, and the application starts with its
    ///     broker down.
    /// </remarks>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _connecting = transport.ConnectAsync(_stopping.Token);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retry = options is null
            ? ConnectRetry.Default
            : new ConnectRetry(TimeSpan.FromMilliseconds(options.ReconnectBaseDelayMs), options.MaxReconnectAttempts);
        if (!await TransportConnectLoop.UntilConnectedAsync(transport, _connecting, retry, logger, stoppingToken)
                .ConfigureAwait(false))
            return;

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
        await _stopping.CancelAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        foreach (var sub in _subscriptions)
            await sub.DisposeAsync().ConfigureAwait(false);

        _subscriptions.Clear();
    }

    public override void Dispose()
    {
        _stopping.Dispose();
        base.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ consumer service started")]
    private partial void LogStarted();

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ consumer service stopped")]
    private partial void LogStopped();

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ consumer subscribed to {Count} message type(s)")]
    private partial void LogSubscribed(int count);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RabbitMQ transport is running with zero subscriptions: published messages will not be consumed")]
    private partial void LogNoSubscriptions();
}
