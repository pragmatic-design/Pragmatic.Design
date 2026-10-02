using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Configuration.Database.Audit;
using Pragmatic.Configuration.Database.Dialects;
using Pragmatic.Configuration.Database.Schema;

namespace Pragmatic.Configuration.Database;

/// <summary>
///     IConfigurationStore backed by a relational database via ADO.NET.
///     Supports PostgreSQL, SQL Server, and SQLite.
/// </summary>
internal sealed partial class DatabaseConfigurationStore(
    IDbConnectionFactory connectionFactory,
    ISqlDialect dialect,
    Audit.ConfigurationAuditRecorder audit,
    ConfigurationSchemaManager schema,
    IOptions<DatabaseConfigurationOptions> options,
    ISensitiveKeyClassifier sensitiveKeys,
    IEnumerable<IConfigurationChangeNotifier> changeNotifiers,
    ILogger<DatabaseConfigurationStore> logger)
    : IConfigurationStore
{
    private readonly DatabaseConfigurationOptions _options = options.Value;

    // Optional provider-native push source (e.g. Postgres LISTEN/NOTIFY). Null → poll-only watch.
    private readonly IConfigurationChangeNotifier? _notifier = changeNotifiers.FirstOrDefault();

    /// <summary>Placeholder written to the audit trail in place of a <c>[Sensitive]</c> value's plaintext.</summary>
    private const string SensitivePlaceholder = "(sensitive)";

    // C3: One-time schema initialization with proper async guard
    private Task? _initTask;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
        => await GetInternalAsync(key, null, ct).ConfigureAwait(false);

    public async Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default)
        => await GetInternalAsync(key, tenantId, ct).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default)
        => await GetSectionInternalAsync(prefix, null, ct).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, string tenantId, CancellationToken ct = default)
        => await GetSectionInternalAsync(prefix, tenantId, ct).ConfigureAwait(false);

    public async Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
    {
        var connection = connectionFactory.CreateConnection();
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await EnsureInitializedAsync(connection, ct).ConfigureAwait(false);

            // Upsert + audit share one transaction so the change and its audit record commit
            // atomically — a failed audit insert must not leave a committed value unaudited.
            var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                // Read old value for audit (inside the transaction for a consistent snapshot).
                var oldValue = await ReadValueAsync(connection, key, tenantId, transaction, ct).ConfigureAwait(false);

                var command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    command.Transaction = transaction;
                    command.CommandText = dialect.SetValue;

                    command.AddParameter("@key", key);
                    command.AddParameter("@value", value);
                    command.AddParameter("@tenantId", tenantId);
                    command.AddParameter("@environment", _options.Environment);
                    command.AddParameter("@updatedBy", _options.AuditUser);

                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                var action = oldValue is null ? "created" : "updated";
                // The trail stores a hash of the previous value, never the value. A [Sensitive] key
                // hashes a placeholder instead: an unsalted hash of a short secret is guessable, and a
                // long-retained record is the wrong place to find that out.
                var masked = sensitiveKeys.IsSensitive(key);
                var previous = masked && oldValue is not null ? SensitivePlaceholder : oldValue;
                await audit.RecordAsync(connection, transaction, "config", key, tenantId, action, previous, _options.AuditUser, ct).ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);
                LogConfigAction(action, key, tenantId ?? "(base)");
            }
        }
    }

    public async Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        var connection = connectionFactory.CreateConnection();
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await EnsureInitializedAsync(connection, ct).ConfigureAwait(false);

            var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var oldValue = await ReadValueAsync(connection, key, tenantId, transaction, ct).ConfigureAwait(false);

                var command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    command.Transaction = transaction;
                    command.CommandText = dialect.DeleteValue;

                    command.AddParameter("@key", key);
                    command.AddParameter("@tenantId", tenantId);
                    command.AddParameter("@environment", _options.Environment);

                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                if (oldValue is not null)
                {
                    var auditOld = sensitiveKeys.IsSensitive(key) ? SensitivePlaceholder : oldValue;
                    await audit.RecordAsync(connection, transaction, "config", key, tenantId, "deleted", auditOld, _options.AuditUser, ct).ConfigureAwait(false);
                }

                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
        }
    }

    public async IAsyncEnumerable<ConfigurationChange> WatchAsync(
        string keyPattern,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Change polling disabled: expose no changes (hot-reload off) rather than polling anyway.
        if (!_options.EnableChangePolling)
            yield break;

        // Native push available (e.g. Postgres LISTEN/NOTIFY): merge push + periodic poll reconcile.
        if (_notifier is not null)
        {
            await foreach (var change in WatchHybridAsync(keyPattern, ct).ConfigureAwait(false))
                yield return change;
            yield break;
        }

        var lastCheck = DateTimeOffset.UtcNow;
        var interval = _options.PollingInterval;

        // One connection reused across polls instead of a fresh connection per cycle. The poll helper
        // health-checks and (re)opens it, and nulls it on transient failure so the next cycle reopens.
        DbConnection? connection = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);

                // Poll work is isolated in a helper (no yield inside try/catch) so a transient
                // failure can dispose the broken connection without aborting the enumeration.
                PollResult poll;
                try
                {
                    poll = await PollChangesAsync(connection, keyPattern, lastCheck, ct).ConfigureAwait(false);
                    connection = poll.Connection;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Transient failure: discard the connection so the next cycle reopens a fresh one.
                    if (connection is not null)
                        await connection.DisposeAsync().ConfigureAwait(false);
                    connection = null;
                    continue;
                }

                foreach (var change in poll.Changes)
                    yield return change;

                lastCheck = poll.QueryTime;
            }
        }
        finally
        {
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Hybrid watch: a provider-native push source (LISTEN/NOTIFY) delivers changes with low latency,
    ///     while a periodic poll reconciles anything a push might have missed (e.g. during a reconnect). Both
    ///     feed one channel; a change may be delivered twice, which is safe because consumers (cache
    ///     invalidation) are idempotent.
    /// </summary>
    private async IAsyncEnumerable<ConfigurationChange> WatchHybridAsync(
        string keyPattern, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<ConfigurationChange>(
            new UnboundedChannelOptions { SingleReader = true });

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = linkedCts.Token;

        var pushTask = PumpPushAsync(channel.Writer, keyPattern, token);
        var pollTask = PumpPollReconcileAsync(channel.Writer, keyPattern, token);

        // Close the channel once both pumps stop (on cancellation). Pumps swallow their own errors, so the
        // completion carries none — a push failure just degrades to poll-only for the rest of the stream.
        _ = Task.WhenAll(pushTask, pollTask)
            .ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return change;
        }
        finally
        {
            linkedCts.Cancel();
        }
    }

    /// <summary>Pumps native push signals into the channel, reading the current value for upserts.</summary>
    private async Task PumpPushAsync(
        ChannelWriter<ConfigurationChange> writer, string keyPattern, CancellationToken ct)
    {
        try
        {
            await foreach (var signal in _notifier!.ListenAsync(ct).ConfigureAwait(false))
            {
                if (!MatchesPattern(signal.Key, keyPattern) || !EnvironmentMatches(signal.Environment))
                    continue;

                var value = signal.Kind == ConfigurationChangeKind.Upsert
                    ? await GetInternalAsync(signal.Key, signal.TenantId, ct).ConfigureAwait(false)
                    : null;

                await writer.WriteAsync(
                    new ConfigurationChange(signal.Key, null, value, signal.TenantId, DateTimeOffset.UtcNow), ct)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            // Degrade to poll-only: the reconcile pump keeps the stream alive.
            LogWatchPushFailed(ex);
        }
    }

    /// <summary>Periodic reconcile poll feeding the same channel, reusing the poll-only cycle logic.</summary>
    private async Task PumpPollReconcileAsync(
        ChannelWriter<ConfigurationChange> writer, string keyPattern, CancellationToken ct)
    {
        var lastCheck = DateTimeOffset.UtcNow;
        var interval = _options.PollingInterval;
        DbConnection? connection = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);

                PollResult poll;
                try
                {
                    poll = await PollChangesAsync(connection, keyPattern, lastCheck, ct).ConfigureAwait(false);
                    connection = poll.Connection;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    if (connection is not null)
                        await connection.DisposeAsync().ConfigureAwait(false);
                    connection = null;
                    continue;
                }

                foreach (var change in poll.Changes)
                    await writer.WriteAsync(change, ct).ConfigureAwait(false);

                lastCheck = poll.QueryTime;
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Whether a signal's environment matches this store's configured environment (null = all).</summary>
    private bool EnvironmentMatches(string? signalEnvironment)
        => _options.Environment is null
           || string.Equals(signalEnvironment, _options.Environment, StringComparison.Ordinal);

    /// <summary>
    ///     Executes a single poll cycle on a reused connection, (re)opening it first if it is null,
    ///     closed, or broken. Returns the (possibly new) connection alongside the matched changes.
    /// </summary>
    private async Task<PollResult> PollChangesAsync(
        DbConnection? connection, string keyPattern, DateTimeOffset lastCheck, CancellationToken ct)
    {
        // Health-check: (re)open when there is no usable connection.
        if (connection is null || connection.State is ConnectionState.Closed or ConnectionState.Broken)
        {
            if (connection is { State: ConnectionState.Broken })
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                connection = null;
            }

            connection ??= connectionFactory.CreateConnection();
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await EnsureInitializedAsync(connection, ct).ConfigureAwait(false);
        }

        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = dialect.GetChangesSince;
            // The dialect formats the cursor to its stored updated_at precision so the
            // `updated_at > @since` comparison stays exact: a change written in the same second
            // as a sub-second cursor is not skipped.
            command.AddParameter("@since", dialect.FormatChangeCursor(lastCheck));
            command.AddParameter("@environment", _options.Environment);

            var changes = new List<ConfigurationChange>();

            // High-water mark taken from the DB's own updated_at values (not the app clock): advancing
            // the cursor by the app clock would drop rows whenever the app clock runs ahead of the DB
            // clock (their updated_at would fall at or below the app-time cursor). Track the newest
            // updated_at actually observed — including rows filtered out by the pattern, so we don't
            // rescan them — and keep the previous cursor when nothing changed.
            DateTimeOffset? highWater = null;

            var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var key = reader.GetString(0);
                    var updatedAt = reader.GetFieldValue<DateTimeOffset>(3);
                    if (highWater is null || updatedAt > highWater)
                        highWater = updatedAt;

                    if (!MatchesPattern(key, keyPattern))
                        continue;

                    var value = reader.GetString(1);
                    var tenantId = reader.IsDBNull(2) ? null : reader.GetString(2);

                    changes.Add(new ConfigurationChange(key, null, value, tenantId, updatedAt));
                }
            }

            return new PollResult(connection, changes, highWater ?? lastCheck);
        }
    }

    private readonly record struct PollResult(
        DbConnection Connection, IReadOnlyList<ConfigurationChange> Changes, DateTimeOffset QueryTime);

    private async Task<string?> GetInternalAsync(string key, string? tenantId, CancellationToken ct)
    {
        var connection = connectionFactory.CreateConnection();
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await EnsureInitializedAsync(connection, ct).ConfigureAwait(false);
            return await ReadValueAsync(connection, key, tenantId, null, ct).ConfigureAwait(false);
        }
    }

    private async Task<string?> ReadValueAsync(
        DbConnection connection, string key, string? tenantId, DbTransaction? transaction, CancellationToken ct)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.Transaction = transaction;
            command.CommandText = dialect.GetValue;

            command.AddParameter("@key", key);
            command.AddParameter("@tenantId", tenantId);
            command.AddParameter("@environment", _options.Environment);

            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result is DBNull or null ? null : (string)result;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> GetSectionInternalAsync(
        string prefix, string? tenantId, CancellationToken ct)
    {
        var connection = connectionFactory.CreateConnection();
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await EnsureInitializedAsync(connection, ct).ConfigureAwait(false);

            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = dialect.GetSection;

                command.AddParameter("@escapedPrefix", dialect.EscapeLikePattern(prefix));
                command.AddParameter("@tenantId", tenantId);
                command.AddParameter("@environment", _options.Environment);

                var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    var result = new Dictionary<string, string>();

                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        result[reader.GetString(0)] = reader.GetString(1);

                    return result;
                }
            }
        }
    }

    /// <summary>
    ///     Ensures the database schema is created exactly once, regardless of concurrent callers.
    /// </summary>
    private async Task EnsureInitializedAsync(DbConnection connection, CancellationToken ct)
    {
        if (_initTask is { IsCompletedSuccessfully: true }) return;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Reset on previous failure to allow retry with a fresh connection
            if (_initTask is { IsFaulted: true })
                _initTask = null;

            _initTask ??= schema.EnsureCreatedAsync(connection, ct);
            await _initTask.ConfigureAwait(false);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static bool MatchesPattern(string key, string pattern)
    {
        if (pattern == "*")
            return true;

        if (pattern.EndsWith('*'))
            return key.StartsWith(pattern[..^1], StringComparison.Ordinal);

        return string.Equals(key, pattern, StringComparison.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Configuration {Action}: {Key} (tenant: {TenantId})")]
    private partial void LogConfigAction(string action, string key, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Configuration change push source failed; degrading to poll-only reconcile for this watch")]
    private partial void LogWatchPushFailed(Exception ex);
}
