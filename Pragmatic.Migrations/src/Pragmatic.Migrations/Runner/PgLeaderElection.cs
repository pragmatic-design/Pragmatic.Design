using System.Data.Common;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     PostgreSQL-based leader election for distributed migrations.
///     Uses pg_try_advisory_lock — non-blocking. First instance to acquire wins.
///     Non-leaders poll until the leader releases (migration complete).
/// </summary>
public sealed class PgLeaderElection(Func<CancellationToken, Task<DbConnection>> connectionFactory)
    : IMigrationLeaderElection, IAsyncDisposable
{
    private const long LockId = 0x5072_6167_4D69_6772; // "PragMigr"

    // Guards the (_connection, _isLeader) pair as a unit. The advisory lock is bound to the
    // session held by _connection, so leadership and the connection must be published/cleared
    // atomically — otherwise DisposeAsync could race TryBecomeLeaderAsync and either leak the
    // connection or unlock/dispose a connection another method is mid-flight on (TOCTOU).
    private readonly Lock _gate = new();
    private DbConnection? _connection;
    private bool _isLeader;

    public async Task<bool> TryBecomeLeaderAsync(CancellationToken ct = default)
    {
        var connection = await connectionFactory(ct).ConfigureAwait(false);
        bool isLeader;
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"SELECT pg_try_advisory_lock({LockId})";
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            isLeader = IsTrueResult(result);
        }

        // Publish the connection + flag atomically once the acquire result is known.
        DbConnection? toDispose = null;
        lock (_gate)
        {
            // If something concurrently disposed us, don't retain the new connection.
            toDispose = _connection;
            _connection = connection;
            _isLeader = isLeader;
        }

        if (toDispose is not null)
            await toDispose.DisposeAsync().ConfigureAwait(false);

        return isLeader;
    }

    public async Task ReleaseLeadershipAsync(CancellationToken ct = default)
    {
        // Atomically read+clear the leadership flag and capture the owning connection.
        DbConnection? conn;
        lock (_gate)
        {
            if (!_isLeader || _connection is null) return;
            _isLeader = false;
            conn = _connection;
        }

        try
        {
            var cmd = conn.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = $"SELECT pg_advisory_unlock({LockId})";
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        catch
        {
            // Lock released on disconnect anyway
        }
    }

    public async Task WaitForLeaderCompletionAsync(CancellationToken ct = default)
    {
        // Non-leader: poll until we can acquire+release (meaning leader is done)
        while (!ct.IsCancellationRequested)
        {
            // Base 500ms poll + randomized jitter to avoid a thundering herd of followers
            // hammering the lock in lockstep. Random.Shared is thread-safe.
            var delay = 500 + Random.Shared.Next(0, 250);
            await Task.Delay(delay, ct).ConfigureAwait(false);

            try
            {
                // Always open a fresh connection per poll; dispose it via the await-using
                // to avoid a leak. Thread the real cancellation token into the factory.
                var pollConn = await connectionFactory(ct).ConfigureAwait(false);
                await using (pollConn.ConfigureAwait(false))
                {
                    var cmd = pollConn.CreateCommand();
                    await using (cmd.ConfigureAwait(false))
                    {
                        cmd.CommandText = $"SELECT pg_try_advisory_lock({LockId})";
                        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                        if (IsTrueResult(result))
                        {
                            // Leader is done — release immediately
                            var release = pollConn.CreateCommand();
                            await using (release.ConfigureAwait(false))
                            {
                                release.CommandText = $"SELECT pg_advisory_unlock({LockId})";
                                await release.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                            }
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Propagate clean cancellation — do not mask it as a normal completion.
                throw;
            }
            catch
            {
                // Connection error — retry
            }
        }

        // Loop exited because cancellation was requested at the top of the while.
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>PostgreSQL returns bool, but some providers return string "t"/"True".</summary>
    private static bool IsTrueResult(object? result) => result switch
    {
        bool b => b,
        string s => s.Equals("t", StringComparison.OrdinalIgnoreCase)
                  || s.Equals("true", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    public async ValueTask DisposeAsync()
    {
        // ReleaseLeadershipAsync atomically clears the flag (unlocking if we held leadership).
        // Then atomically detach the connection so we dispose it exactly once and never race a
        // concurrent TryBecomeLeaderAsync that may be publishing a fresh connection.
        await ReleaseLeadershipAsync().ConfigureAwait(false);

        DbConnection? conn;
        lock (_gate)
        {
            conn = _connection;
            _connection = null;
        }

        if (conn is not null)
            await conn.DisposeAsync().ConfigureAwait(false);
    }
}
