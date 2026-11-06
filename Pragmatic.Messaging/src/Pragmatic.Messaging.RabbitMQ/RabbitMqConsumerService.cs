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

        // The subscriptions are this method's, closed by it whenever its binds return: StopAsync returns when the stop's token fires, with
        // a bind possibly still running, so closing them there missed the ones bound after it (#551).
        var bound = new List<IAsyncDisposable>();
        try
        {
            bound.AddRange(
                await TransportSubscriptionBinder.BindAsync(transport, router, scopeFactory, subscriptions, killSwitch, logger, busName: null, messaging?.Value.SubscriberName, stoppingToken)
                    .ConfigureAwait(false));

            // Distributed request/reply: bind the SG-generated request executors (responder side).
            bound.AddRange(
                await Pragmatic.Messaging.RequestReply.RequestReplyBinder.BindAsync(transport, scopeFactory, requestSubscriptions, logger, stoppingToken)
                    .ConfigureAwait(false));

            if (bound.Count == 0)
                LogNoSubscriptions();
            else
                LogSubscribed(bound.Count);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
        }
        finally
        {
            await TransportSubscriptionBinder.DisposeAllAsync(bound).ConfigureAwait(false);
        }

        await transport.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        LogStopped();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
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
