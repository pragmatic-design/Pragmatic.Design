using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging;

/// <summary>
///     Consumer service for one NAMED bus: connects the bus's isolated transport and binds only
///     the subscriptions assigned to it (handlers with <c>[OnBus("{name}")]</c>). Dispatches with
///     <see cref="MessageContext.BusName"/> stamped so local handler filtering targets exactly
///     this bus's handlers.
/// </summary>
public sealed partial class NamedBusConsumerService(
    string busName,
    IMessageTransport transport,
    IServiceScopeFactory scopeFactory,
    IMessageRouter router,
    IEnumerable<MessageSubscription> subscriptions,
    ILogger<NamedBusConsumerService> logger,
    KillSwitchOptions? killSwitch = null,
    IOptions<MessagingOptions>? messaging = null,
    ConnectRetry? retry = null)
    : BackgroundService
{
    private readonly List<IAsyncDisposable> _subscriptions = [];
    private readonly CancellationTokenSource _stopping = new();
    private Task _connecting = Task.CompletedTask;

    /// <summary>Begins the connect before the host reports started, then starts the binding loop.</summary>
    /// <remarks>
    ///     On .NET 10 a BackgroundService runs all of ExecuteAsync in the background, so a connect begun there
    ///     would not have begun when the host finishes starting. Begun here, a transport whose connect
    ///     does no I/O (Kafka, Service Bus) is Connected by then, and one whose connect does (RabbitMQ, SQL)
    ///     is Connecting, which its publish waits for. Not awaited: the application starts with a broker down.
    /// </remarks>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _connecting = transport.ConnectAsync(_stopping.Token);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await TransportConnectLoop.UntilConnectedAsync(
                transport, _connecting, retry ?? ConnectRetry.Default, logger, stoppingToken).ConfigureAwait(false))
            return;

        LogStarted(busName, transport.Name);

        _subscriptions.AddRange(
            await TransportSubscriptionBinder.BindAsync(
                transport, router, scopeFactory, subscriptions, killSwitch, logger, busName,
                messaging?.Value.SubscriberName, stoppingToken)
                .ConfigureAwait(false));

        LogSubscribed(busName, _subscriptions.Count);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }

        await transport.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        LogStopped(busName);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Named bus '{BusName}' consumer started (transport: {Transport})")]
    private partial void LogStarted(string busName, string transport);

    [LoggerMessage(Level = LogLevel.Information, Message = "Named bus '{BusName}' subscribed to {Count} message type(s)")]
    private partial void LogSubscribed(string busName, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Named bus '{BusName}' consumer stopped")]
    private partial void LogStopped(string busName);
}
