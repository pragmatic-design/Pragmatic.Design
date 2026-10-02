using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Provider-agnostic leader election using a <c>__PragmaticLock</c> table.
///     Works on PostgreSQL, SQL Server, and SQLite. First instance to claim the
///     row wins; others poll until the leader releases or the lock expires.
/// </summary>
public sealed class DatabaseLeaderElection : IMigrationLeaderElection
{
    private readonly Func<Task<DbConnection>> _connectionFactory;
    private readonly string _providerName;
    private readonly ILogger _logger;
    private readonly TimeSpan _lockTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly string _hostId;

    // Guards the leadership flag. Each operation opens and disposes its own connection, so the
    // only mutable shared state is the flag itself; a lock keeps the read-modify-write atomic
    // and prevents a TryBecomeLeader/ReleaseLeadership interleaving from publishing a torn view.
    private readonly Lock _gate = new();
    private bool _isLeader;

    private bool IsLeader
    {
        get { lock (_gate) return _isLeader; }
        set { lock (_gate) _isLeader = value; }
    }

    public DatabaseLeaderElection(
        Func<Task<DbConnection>> connectionFactory,
        string providerName,
        ILogger<DatabaseLeaderElection> logger,
        TimeSpan? lockTimeout = null,
        string? hostId = null,
        TimeSpan? pollInterval = null)
    {
        _connectionFactory = connectionFactory;
        _providerName = providerName;
        _logger = logger;
        _lockTimeout = lockTimeout ?? TimeSpan.FromMinutes(5);
        _pollInterval = pollInterval is { } p && p > TimeSpan.Zero ? p : TimeSpan.FromSeconds(2);
        _hostId = hostId ?? Guid.CreateVersion7().ToString("N")[..16];
    }

    public async Task<bool> TryBecomeLeaderAsync(CancellationToken ct = default)
    {
        var conn = await _connectionFactory().ConfigureAwait(false);
        await using (conn.ConfigureAwait(false))
        {
            await EnsureLockTableAsync(conn, ct).ConfigureAwait(false);

            var cmd = conn.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = BuildTryAcquireSql();
                AddParameter(cmd, "@holderId", _hostId);
                AddParameter(cmd, "@timeoutMinutes", (int)_lockTimeout.TotalMinutes);

                var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                IsLeader = rows > 0;
            }
        }

        var elected = IsLeader;
        if (elected)
            _logger.LogInformation("Migration leader elected: {HostId}", _hostId);
        else
            _logger.LogInformation("Another instance is the migration leader — waiting");

        return elected;
    }

    public async Task ReleaseLeadershipAsync(CancellationToken ct = default)
    {
        // Atomically clear the flag and learn whether we actually held leadership, so a
        // concurrent caller can never both release the same lock or skip a needed release.
        bool wasLeader;
        lock (_gate)
        {
            wasLeader = _isLeader;
            _isLeader = false;
        }

        if (!wasLeader) return;

        try
        {
            var conn = await _connectionFactory().ConfigureAwait(false);
            await using (conn.ConfigureAwait(false))
            {
                var cmd = conn.CreateCommand();
                await using (cmd.ConfigureAwait(false))
                {
                    cmd.CommandText = BuildReleaseSql();
                    AddParameter(cmd, "@holderId", _hostId);
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("Migration leadership released: {HostId}", _hostId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release migration leadership — lock will expire after timeout");
        }
    }

    public async Task WaitForLeaderCompletionAsync(CancellationToken ct = default)
    {
        // Cap consecutive polling FAILURES so a follower can't deadlock forever when the lock
        // can't be read (unreachable DB, wrong dialect, missing lock table). Persistent errors here used
        // to be swallowed and retried indefinitely; now we give up by throwing, which the fail-safe
        // FallbackLeaderElection wrapper catches → the follower stops waiting instead of hanging. A
        // legitimately-held lock returns a non-null holder (not an error) and keeps waiting as before.
        const int maxConsecutiveErrors = 5;
        var consecutiveErrors = 0;
        while (!ct.IsCancellationRequested)
        {
            // Configured poll interval + randomized jitter to avoid a thundering herd of
            // followers all re-checking the lock on the same cadence. Random.Shared is thread-safe.
            var baseMs = (int)Math.Clamp(_pollInterval.TotalMilliseconds, 100, int.MaxValue);
            var jitterMs = Random.Shared.Next(0, Math.Max(1, baseMs / 4));
            await Task.Delay(baseMs + jitterMs, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (ct.IsCancellationRequested) return;

            try
            {
                var conn = await _connectionFactory().ConfigureAwait(false);
                await using (conn.ConfigureAwait(false))
                {
                    var cmd = conn.CreateCommand();
                    await using (cmd.ConfigureAwait(false))
                    {
                        cmd.CommandText = BuildCheckSql();
                        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);

                        // NULL = no holder (leader released or expired), we can proceed
                        if (result is null or DBNull)
                        {
                            _logger.LogInformation("Migration leader completed — proceeding");
                            return;
                        }
                    }
                }

                consecutiveErrors = 0; // a successful poll (holder still present) — keep waiting
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error polling migration lock — retrying");
                if (++consecutiveErrors >= maxConsecutiveErrors)
                    throw new InvalidOperationException(
                        $"Migration leader-completion polling failed {maxConsecutiveErrors} times consecutively " +
                        "(unreachable database, wrong provider dialect, or missing lock table); giving up.", ex);
            }
        }
    }

    // --- SQL generation (provider-aware) ---

    /// <summary>How many times creating the lock table is tried when another instance is creating it too.</summary>
    private const int EnsureAttempts = 3;

    /// <summary>
    ///     Creates the lock table and its row if they are missing, even while another instance does the same.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <c>IF NOT EXISTS</c> is not safe against a concurrent create. On PostgreSQL the second of two
    ///         sessions waits for the first's transaction and then fails with <c>23505</c> on
    ///         <c>pg_type_typname_nsp_index</c>; SQL Server's <c>IF NOT EXISTS … CREATE</c> and its seed have the
    ///         same window. That error made the whole election throw, so two instances starting together on a
    ///         new database left one of them out of the election.
    ///     </para>
    ///     <para>
    ///         Tried again because by then the other session has committed, and the same statements find the
    ///         table and the row there. An error that is not that race — no permission, no database — fails
    ///         the same way every time and surfaces after the last attempt.
    ///     </para>
    /// </remarks>
    private async Task EnsureLockTableAsync(DbConnection conn, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await CreateLockTableAndRowAsync(conn, ct).ConfigureAwait(false);
                return;
            }
            catch (DbException ex) when (attempt < EnsureAttempts)
            {
                _logger.LogInformation(ex,
                    "Creating the migration lock table raced another instance — trying again (attempt {Attempt})", attempt);
            }
        }
    }

    private async Task CreateLockTableAndRowAsync(DbConnection conn, CancellationToken ct)
    {
        var cmd = conn.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = BuildCreateTableSql();
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Ensure seed row exists
        var seed = conn.CreateCommand();
        await using (seed.ConfigureAwait(false))
        {
            seed.CommandText = BuildSeedSql();
            await seed.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private string BuildCreateTableSql() => _providerName switch
    {
        "SqlServer" => """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '__PragmaticLock')
            CREATE TABLE __PragmaticLock (
                LockName    NVARCHAR(128) NOT NULL PRIMARY KEY,
                HolderId    NVARCHAR(64)  NULL,
                AcquiredAt  DATETIMEOFFSET NULL,
                ExpiresAt   DATETIMEOFFSET NULL
            )
            """,
        "Sqlite" => """
            CREATE TABLE IF NOT EXISTS __PragmaticLock (
                LockName    TEXT NOT NULL PRIMARY KEY,
                HolderId    TEXT NULL,
                AcquiredAt  TEXT NULL,
                ExpiresAt   TEXT NULL
            )
            """,
        _ => // PostgreSQL (default)
            """
            CREATE TABLE IF NOT EXISTS "__PragmaticLock" (
                "LockName"    VARCHAR(128) NOT NULL PRIMARY KEY,
                "HolderId"    VARCHAR(64)  NULL,
                "AcquiredAt"  TIMESTAMPTZ  NULL,
                "ExpiresAt"   TIMESTAMPTZ  NULL
            )
            """,
    };

    private string BuildSeedSql() => _providerName switch
    {
        "SqlServer" => """
            IF NOT EXISTS (SELECT 1 FROM __PragmaticLock WHERE LockName = 'migration')
            INSERT INTO __PragmaticLock (LockName) VALUES ('migration')
            """,
        "Sqlite" =>
            "INSERT OR IGNORE INTO __PragmaticLock (LockName) VALUES ('migration')",
        _ => // PostgreSQL
            """INSERT INTO "__PragmaticLock" ("LockName") VALUES ('migration') ON CONFLICT ("LockName") DO NOTHING""",
    };

    private string BuildTryAcquireSql() => _providerName switch
    {
        "SqlServer" => """
            UPDATE __PragmaticLock
            SET HolderId = @holderId,
                AcquiredAt = SYSUTCDATETIME(),
                ExpiresAt = DATEADD(MINUTE, @timeoutMinutes, SYSUTCDATETIME())
            WHERE LockName = 'migration'
              AND (HolderId IS NULL OR HolderId = @holderId OR ExpiresAt < SYSUTCDATETIME())
            """,
        "Sqlite" => """
            UPDATE __PragmaticLock
            SET HolderId = @holderId,
                AcquiredAt = datetime('now'),
                ExpiresAt = datetime('now', '+' || @timeoutMinutes || ' minutes')
            WHERE LockName = 'migration'
              AND (HolderId IS NULL OR HolderId = @holderId OR ExpiresAt < datetime('now'))
            """,
        _ => // PostgreSQL
            """
            UPDATE "__PragmaticLock"
            SET "HolderId" = @holderId,
                "AcquiredAt" = NOW(),
                "ExpiresAt" = NOW() + MAKE_INTERVAL(mins => @timeoutMinutes)
            WHERE "LockName" = 'migration'
              AND ("HolderId" IS NULL OR "HolderId" = @holderId OR "ExpiresAt" < NOW())
            """,
    };

    private string BuildReleaseSql() => _providerName switch
    {
        "SqlServer" => """
            UPDATE __PragmaticLock
            SET HolderId = NULL, AcquiredAt = NULL, ExpiresAt = NULL
            WHERE LockName = 'migration' AND HolderId = @holderId
            """,
        "Sqlite" => """
            UPDATE __PragmaticLock
            SET HolderId = NULL, AcquiredAt = NULL, ExpiresAt = NULL
            WHERE LockName = 'migration' AND HolderId = @holderId
            """,
        _ => // PostgreSQL
            """
            UPDATE "__PragmaticLock"
            SET "HolderId" = NULL, "AcquiredAt" = NULL, "ExpiresAt" = NULL
            WHERE "LockName" = 'migration' AND "HolderId" = @holderId
            """,
    };

    // Returns the current HolderId when the lock IS held and not yet expired.
    // WaitForLeaderCompletionAsync loops until the result is NULL/DBNull (lock released/expired).
    private string BuildCheckSql() => _providerName switch
    {
        "SqlServer" =>
            "SELECT HolderId FROM __PragmaticLock WHERE LockName = 'migration' AND HolderId IS NOT NULL AND ExpiresAt >= SYSUTCDATETIME()",
        "Sqlite" =>
            "SELECT HolderId FROM __PragmaticLock WHERE LockName = 'migration' AND HolderId IS NOT NULL AND ExpiresAt >= datetime('now')",
        _ => // PostgreSQL
            """SELECT "HolderId" FROM "__PragmaticLock" WHERE "LockName" = 'migration' AND "HolderId" IS NOT NULL AND "ExpiresAt" >= NOW()""",
    };

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        cmd.Parameters.Add(param);
    }
}
