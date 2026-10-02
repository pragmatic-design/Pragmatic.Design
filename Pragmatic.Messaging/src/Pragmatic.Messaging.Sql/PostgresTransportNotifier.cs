using Microsoft.Extensions.Logging;
using Npgsql;

namespace Pragmatic.Messaging.Sql;

/// <summary>
///     Low-latency wakeups on PostgreSQL: a dedicated LISTEN connection receives
///     <c>pg_notify(channel, queueName)</c> fired by publishers after commit and wakes the
///     matching queue consumer. Purely an accelerator — polling stays active as the safety net,
///     so a dropped connection costs latency, never messages. Reconnects with capped backoff.
/// </summary>
public sealed partial class PostgresTransportNotifier(
    string connectionString,
    string channel,
    Action<string> onWake,
    ILogger<PostgresTransportNotifier> logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenLoop;

    public void Start() => _listenLoop = Task.Run(ListenAsync);

    private async Task ListenAsync()
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var connection = new NpgsqlConnection(connectionString);
                await using (connection.ConfigureAwait(false))
                {
                    connection.Notification += (_, args) => onWake(args.Payload);

                    await connection.OpenAsync(_cts.Token).ConfigureAwait(false);
                    var listen = connection.CreateCommand();
                    await using (listen.ConfigureAwait(false))
                    {
                        listen.CommandText = $"LISTEN \"{channel}\"";
                        await listen.ExecuteNonQueryAsync(_cts.Token).ConfigureAwait(false);
                    }

                    LogListening(channel);
                    backoff = TimeSpan.FromSeconds(1);

                    while (!_cts.IsCancellationRequested)
                        await connection.WaitAsync(_cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogListenLost(channel, backoff, ex);
                try
                {
                    await Task.Delay(backoff, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_listenLoop is not null)
        {
            try
            {
                await _listenLoop.ConfigureAwait(false);
            }
            catch
            {
                // Loop already logged.
            }
        }

        _cts.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SQL transport LISTEN active on channel {Channel} (pg_notify wakeups)")]
    private partial void LogListening(string channel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SQL transport LISTEN connection lost on {Channel}; reconnecting in {Backoff} (polling remains active — latency only)")]
    private partial void LogListenLost(string channel, TimeSpan backoff, Exception ex);
}
