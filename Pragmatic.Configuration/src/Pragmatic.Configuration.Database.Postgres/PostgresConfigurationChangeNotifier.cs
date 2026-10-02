using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Pragmatic.Configuration.Database.Postgres;

/// <summary>
///     PostgreSQL push source for configuration changes: a dedicated <c>LISTEN pragmatic_config_change</c>
///     connection receives the <c>pg_notify</c> payloads fired by the schema trigger and turns them into
///     <see cref="ConfigurationChangeSignal" />s. Unlike polling it also reports DELETEs. Purely an
///     accelerator — the store keeps a periodic poll reconcile — so a dropped connection costs latency, never
///     correctness. Reconnects with capped backoff.
/// </summary>
public sealed partial class PostgresConfigurationChangeNotifier(
    IDbConnectionFactory connectionFactory,
    ILogger<PostgresConfigurationChangeNotifier> logger) : IConfigurationChangeNotifier
{
    private const string NotifyChannel = "pragmatic_config_change";

    public async IAsyncEnumerable<ConfigurationChangeSignal> ListenAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<ConfigurationChangeSignal>(
            new UnboundedChannelOptions { SingleReader = true });

        var loop = Task.Run(() => ListenLoopAsync(channel.Writer, ct), ct);

        try
        {
            await foreach (var signal in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return signal;
        }
        finally
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch
            {
                // The loop logs its own failures; nothing to add on teardown.
            }
        }
    }

    private async Task ListenLoopAsync(ChannelWriter<ConfigurationChangeSignal> writer, CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (connectionFactory.CreateConnection() is not NpgsqlConnection connection)
                    {
                        // The registered factory is not Npgsql: native push can't work here. Stop and let the
                        // store's poll reconcile carry the watch.
                        LogNotNpgsql();
                        return;
                    }

                    await using (connection.ConfigureAwait(false))
                    {
                        connection.Notification += (_, args) =>
                        {
                            if (TryParse(args.Payload, out var signal))
                                writer.TryWrite(signal);
                        };

                        await connection.OpenAsync(ct).ConfigureAwait(false);

                        var listen = connection.CreateCommand();
                        await using (listen.ConfigureAwait(false))
                        {
                            listen.CommandText = $"LISTEN \"{NotifyChannel}\"";
                            await listen.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }

                        LogListening();
                        backoff = TimeSpan.FromSeconds(1);

                        while (!ct.IsCancellationRequested)
                            await connection.WaitAsync(ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogListenLost(backoff, ex);
                    try
                    {
                        await Task.Delay(backoff, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
                }
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    /// <summary>Parses the trigger's JSON payload <c>{"key","tenant","env","op"}</c> into a signal.</summary>
    internal static bool TryParse(string payload, out ConfigurationChangeSignal signal)
    {
        signal = null!;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (!root.TryGetProperty("key", out var keyEl) || keyEl.ValueKind != JsonValueKind.String)
                return false;

            var key = keyEl.GetString()!;
            var tenant = ReadNullableString(root, "tenant");
            var env = ReadNullableString(root, "env");
            var op = ReadNullableString(root, "op");
            var kind = string.Equals(op, "delete", StringComparison.Ordinal)
                ? ConfigurationChangeKind.Delete
                : ConfigurationChangeKind.Upsert;

            signal = new ConfigurationChangeSignal(key, tenant, env, kind);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadNullableString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Configuration LISTEN active on pragmatic_config_change (pg_notify push)")]
    private partial void LogListening();

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Configuration LISTEN connection lost; reconnecting in {Backoff} (poll reconcile remains active — latency only)")]
    private partial void LogListenLost(TimeSpan backoff, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Configuration change factory is not Npgsql; native LISTEN/NOTIFY push disabled (poll reconcile remains active)")]
    private partial void LogNotNpgsql();
}
