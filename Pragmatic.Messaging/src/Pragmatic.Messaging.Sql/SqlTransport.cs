using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Messaging.Sql;

/// <summary>
///     SQL implementation of <see cref="IMessageTransport"/> — durable queues on PostgreSQL or
///     SQL Server tables, no broker. Publish fan-outs one row per durable subscription; send is
///     point-to-point; failed handlers back off on <c>VisibleAt</c> and dead-letter natively
///     after <c>MaxDeliveryCount</c>. On PostgreSQL, publishers fire <c>pg_notify</c> after
///     commit so consumers wake immediately (polling stays the safety net).
/// </summary>
public sealed partial class SqlTransport(
    SqlTransportStorage storage,
    SqlTransportSchema schema,
    IDbContextFactory<SqlTransportDbContext> contextFactory,
    SqlTransportOptions options,
    ILoggerFactory loggerFactory) : IMessageTransport, IQueueConsumerTransport
{
    private readonly ILogger<SqlTransport> _logger = loggerFactory.CreateLogger<SqlTransport>();
    private readonly ConcurrentDictionary<string, SqlQueueConsumer> _consumers = new();
    private readonly ConnectionGate _gate = new();

    private PostgresTransportNotifier? _notifier;
    private volatile bool _isPostgres;
    private volatile TransportStatus _status = TransportStatus.Disconnected;
    private int _disposed;

    /// <inheritdoc />
    public string Name => "Sql";

    /// <inheritdoc />
    public TransportStatus Status => _status;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _gate.Begin();
        _status = TransportStatus.Connecting;
        try
        {
            string? connectionString = null;
            var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            await using (db.ConfigureAwait(false))
            {
                _isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
                connectionString = db.Database.GetConnectionString();
            }

            if (options.AutoCreateSchema)
                await schema.EnsureCreatedAsync(ct).ConfigureAwait(false);

            if (_isPostgres && options.UseNotifications && connectionString is not null)
            {
                _notifier = new PostgresTransportNotifier(
                    connectionString,
                    options.NotificationChannel,
                    WakeConsumer,
                    loggerFactory.CreateLogger<PostgresTransportNotifier>());
                _notifier.Start();
            }

            _status = TransportStatus.Connected;
            _gate.Opened();
            LogConnected(_isPostgres ? "PostgreSQL" : "SQL Server/other");
        }
        catch (Exception ex)
        {
            _status = TransportStatus.Faulted;
            _gate.Failed(ex);
            LogConnectionFailed(ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        foreach (var consumer in _consumers.Values)
            await consumer.DisposeAsync().ConfigureAwait(false);
        _consumers.Clear();

        if (_notifier is not null)
        {
            await _notifier.DisposeAsync().ConfigureAwait(false);
            _notifier = null;
        }

        _status = TransportStatus.Disconnected;
        LogDisconnected();
    }

    /// <inheritdoc />
    public async Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        var enqueuedQueues = await storage.PublishAsync(payload.ToArray(), topic, context, ct: ct).ConfigureAwait(false);
        // Wake exactly the subscription queues the fan-out targeted.
        foreach (var queue in enqueuedQueues)
            await NotifyAsync(queue, ct).ConfigureAwait(false);
        LogPublished(topic, context.MessageId, enqueuedQueues.Count);
    }

    /// <inheritdoc />
    public async Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        await storage.SendAsync(payload.ToArray(), queue, context, ct: ct).ConfigureAwait(false);
        await NotifyAsync(queue, ct).ConfigureAwait(false);
        LogSent(queue, context.MessageId);
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        string subscriptionName,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        // Durable registration: fan-out targets this subscription from now on, even while
        // the consumer is down (ASB subscription semantics).
        await storage.UpsertSubscriptionAsync(topic, subscriptionName, ct).ConfigureAwait(false);
        return StartConsumer(subscriptionName, handler);
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable> SubscribeQueueAsync(
        string queue,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
        CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        // Point-to-point: no registry involvement — sends address the queue row directly.
        return StartConsumer(queue, handler);
    }

    private IAsyncDisposable StartConsumer(
        string queue,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler)
    {
        var consumer = _consumers.GetOrAdd(queue, q => new SqlQueueConsumer(
            storage, options, q, handler, loggerFactory.CreateLogger<SqlQueueConsumer>()));
        LogSubscribed(queue);
        return new SqlSubscription(this, consumer);
    }

    private void WakeConsumer(string queue)
    {
        if (_consumers.TryGetValue(queue, out var consumer))
            consumer.Wake();
    }

    /// <summary>
    ///     Post-commit <c>pg_notify</c> — the row is durable before the notify, so a crash in
    ///     between costs one poll interval, never a message. Notify targets the topic's
    ///     subscription queues (fan-out) or the queue itself. No-op on non-Postgres providers.
    /// </summary>
    private async Task NotifyAsync(string destination, CancellationToken ct)
    {
        if (!_isPostgres || _notifier is null)
            return;

        try
        {
            var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            await using (db.ConfigureAwait(false))
            {
                await db.Database.ExecuteSqlAsync(
                    $"SELECT pg_notify({options.NotificationChannel}, {destination})", ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Latency-only failure: polling delivers the message anyway.
            LogNotifyFailed(destination, ex);
        }
    }

    /// <summary>
    ///     Connected, or waits for the connect in progress (<see cref="SqlTransportOptions.ConnectWaitTimeout" />):
    ///     the consumer service connects in the background, and an operation issued meanwhile is not a
    ///     mistake of its caller.
    /// </summary>
    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_status == TransportStatus.Connecting)
            await _gate.WaitAsync(Name, options.ConnectWaitTimeout, ct).ConfigureAwait(false);

        if (_status != TransportStatus.Connected)
            throw new InvalidOperationException("SQL transport is not connected. Call ConnectAsync first.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await DisconnectAsync().ConfigureAwait(false);
    }

    private sealed class SqlSubscription(SqlTransport transport, SqlQueueConsumer consumer) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            // Stops the LOCAL consumer only — the durable registration stays (fan-out keeps
            // accumulating rows for this subscription while it is down).
            transport._consumers.TryRemove(consumer.Queue, out _);
            await consumer.DisposeAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SQL transport connected ({Provider})")]
    private partial void LogConnected(string provider);

    [LoggerMessage(Level = LogLevel.Error, Message = "SQL transport failed to connect")]
    private partial void LogConnectionFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "SQL transport disconnected")]
    private partial void LogDisconnected();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Published to topic {Topic} (id: {MessageId}, fan-out: {Enqueued} row(s))")]
    private partial void LogPublished(string topic, string messageId, int enqueued);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sent to queue {Queue} (id: {MessageId})")]
    private partial void LogSent(string queue, string messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "SQL consumer bound to queue {Queue}")]
    private partial void LogSubscribed(string queue);

    [LoggerMessage(Level = LogLevel.Debug, Message = "pg_notify failed for {Destination} — polling covers delivery (latency only)")]
    private partial void LogNotifyFailed(string destination, Exception ex);
}
