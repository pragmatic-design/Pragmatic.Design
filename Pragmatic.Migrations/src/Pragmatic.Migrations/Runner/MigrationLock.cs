using System.Data.Common;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Distributed migration lock using database-level advisory locks.
///     Prevents concurrent migration attempts on the same database.
///     PostgreSQL: pg_advisory_lock. SQL Server: sp_getapplock. SQLite: no-op (single writer).
/// </summary>
public static class MigrationLock
{
    // FNV-1a hash of "PragmaticMigrations" → stable lock ID
    private const long LockId = 0x5072_6167_4D69_6772; // "PragMigr"

    /// <summary>Default SQL Server <c>sp_getapplock</c> timeout when none is supplied.</summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Acquires an advisory lock for migrations. Returns a disposable that releases it.
    ///     Blocks until the lock is available or <paramref name="lockTimeout"/> is reached.
    /// </summary>
    /// <param name="connection">The open database connection used to take the advisory lock.</param>
    /// <param name="providerName">The database provider name (selects the locking strategy).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="lockTimeout">
    ///     SQL Server <c>sp_getapplock</c> acquisition timeout. Defaults to 60 seconds when null.
    ///     Ignored by PostgreSQL (blocking session lock) and SQLite (single-writer).
    /// </param>
    public static async Task<IAsyncDisposable> AcquireAsync(
        DbConnection connection, string providerName, CancellationToken ct = default,
        TimeSpan? lockTimeout = null)
    {
        switch (providerName)
        {
            case MigrationConstants.ProviderPostgreSql:
                return await AcquirePostgreSqlLockAsync(connection, ct).ConfigureAwait(false);

            case MigrationConstants.ProviderSqlServer:
                return await AcquireSqlServerLockAsync(
                    connection, lockTimeout ?? DefaultLockTimeout, ct).ConfigureAwait(false);

            default:
                // SQLite: single-writer by default — no advisory lock needed
                return NoOpLock.Instance;
        }
    }

    private static async Task<IAsyncDisposable> AcquirePostgreSqlLockAsync(DbConnection connection, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"SELECT pg_advisory_lock({LockId})";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return new PostgreSqlLockRelease(connection);
    }

    private static async Task<IAsyncDisposable> AcquireSqlServerLockAsync(
        DbConnection connection, TimeSpan lockTimeout, CancellationToken ct)
    {
        // sp_getapplock takes the timeout in milliseconds. Clamp to non-negative; a negative
        // value would otherwise mean "wait indefinitely", which we never want for migrations.
        var timeoutMs = lockTimeout < TimeSpan.Zero ? 0 : (long)lockTimeout.TotalMilliseconds;
        var timeoutParam = timeoutMs > int.MaxValue ? int.MaxValue : (int)timeoutMs;

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            // @LockOwner MUST be 'Session'. The default is 'Transaction', which requires an open
            // transaction — and this lock is taken before the migration transaction begins (it has
            // to guard introspection and diffing too) and released after it commits. With the
            // default the call failed outright with "You attempted to acquire a transactional
            // application lock without an active transaction", so no SQL Server migration could
            // ever get past this point.
            cmd.CommandText = """
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = 'PragmaticMigrations', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @lockTimeoutMs;
                IF @result < 0 THROW 50000, 'Could not acquire migration lock', 1;
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "@lockTimeoutMs";
            p.Value = timeoutParam;
            cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return new SqlServerLockRelease(connection);
    }

    private sealed class PostgreSqlLockRelease(DbConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                var cmd = connection.CreateCommand();
                await using (cmd.ConfigureAwait(false))
                {
                    cmd.CommandText = $"SELECT pg_advisory_unlock({LockId})";
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // Lock is session-scoped — will be released on disconnect
            }
        }
    }

    private sealed class SqlServerLockRelease(DbConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                var cmd = connection.CreateCommand();
                await using (cmd.ConfigureAwait(false))
                {
                    // Must match the owner used on acquisition, otherwise the release is a no-op
                    // and the lock lingers until the session ends.
                    cmd.CommandText = "EXEC sp_releaseapplock @Resource = 'PragmaticMigrations', @LockOwner = 'Session'";
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // Lock is session-scoped — will be released on disconnect
            }
        }
    }

    private sealed class NoOpLock : IAsyncDisposable
    {
        public static readonly NoOpLock Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
