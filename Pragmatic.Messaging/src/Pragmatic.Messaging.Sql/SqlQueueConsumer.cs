using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Sql.Entities;

namespace Pragmatic.Messaging.Sql;

/// <summary>
///     Poll loop for ONE queue: claims a batch, invokes the handler per message (ack = delete,
///     failure = nack → backoff/dead-letter), then waits adaptively — base interval growing to
///     <c>MaxPollingInterval</c> on consecutive empty polls, immediate re-poll on a full batch,
///     and a <see cref="Wake"/> signal (pg_notify) that short-circuits the wait. Polling is
///     always the safety net: correctness never depends on notifications.
/// </summary>
public sealed partial class SqlQueueConsumer : IAsyncDisposable
{
    private readonly SqlTransportStorage _storage;
    private readonly SqlTransportOptions _options;
    private readonly string _queue;
    private readonly Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> _handler;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public SqlQueueConsumer(
        SqlTransportStorage storage,
        SqlTransportOptions options,
        string queue,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
        ILogger logger)
    {
        _storage = storage;
        _options = options;
        _queue = queue;
        _handler = handler;
        _logger = logger;
        _loop = Task.Run(RunAsync);
    }

    /// <summary>The queue this consumer drains (notifier wake routing).</summary>
    public string Queue => _queue;

    /// <summary>Short-circuits the current poll wait (called by the Postgres notifier).</summary>
    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake is already pending — coalesce.
        }
    }

    private async Task RunAsync()
    {
        LogStarted(_queue);
        var interval = _options.PollingInterval;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var batch = await _storage.ClaimBatchAsync(_queue, _cts.Token).ConfigureAwait(false);

                foreach (var message in batch)
                {
                    if (_cts.IsCancellationRequested)
                        break;
                    await ProcessAsync(message).ConfigureAwait(false);
                }

                if (batch.Count >= _options.BatchSize)
                {
                    interval = _options.PollingInterval; // drain: full batch → immediate re-poll
                    continue;
                }

                interval = batch.Count > 0
                    ? _options.PollingInterval
                    : Min(interval + _options.PollingInterval, _options.MaxPollingInterval);

                await _wake.WaitAsync(interval, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Storage hiccup (connection loss): log and keep polling.
                LogPollError(_queue, ex);
                try
                {
                    await Task.Delay(_options.MaxPollingInterval, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        LogStopped(_queue);
    }

    private async Task ProcessAsync(TransportMessage message)
    {
        var context = ToContext(message);
        try
        {
            // CancellationToken.None: shutdown must not interrupt in-flight handlers.
            await _handler(message.Payload, context, CancellationToken.None).ConfigureAwait(false);
            await _storage.AckAsync(message.Id, message.LockedBy!, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogHandlerError(_queue, message.MessageId, ex);
            await _storage.NackAsync(message, ex.Message, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static MessageContext ToContext(TransportMessage message) => new(
        MessageId: message.MessageId,
        CorrelationId: message.CorrelationId,
        TenantId: message.TenantId,
        UserId: message.UserId,
        Headers: message.HeadersJson is null
            ? null
            : JsonSerializer.Deserialize(message.HeadersJson, SqlTransportJsonContext.Default.DictionaryStringString),
        RetryCount: Math.Max(0, message.DeliveryCount - 1),
        SourceBoundary: message.SourceBoundary,
        BusName: message.BusName,
        EnqueuedAt: message.EnqueuedAt);

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        Wake();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch
        {
            // Loop already logged its own errors.
        }

        _cts.Dispose();
        _wake.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "SQL consumer started on queue {Queue}")]
    private partial void LogStarted(string queue);

    [LoggerMessage(Level = LogLevel.Debug, Message = "SQL consumer stopped on queue {Queue}")]
    private partial void LogStopped(string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "SQL consumer poll error on queue {Queue}")]
    private partial void LogPollError(string queue, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handler error on queue {Queue} (message {MessageId}) — nacked (backoff or dead-letter)")]
    private partial void LogHandlerError(string queue, string messageId, Exception ex);
}
