using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Pragmatic.Persistence.EFCore.Sequences;

/// <summary>
///     Fetches the next value of a database sequence for an <c>[GeneratedValue]</c> <c>{SEQ:N}</c> token.
///     Sequence generation is provider-specific and concurrency-safe: gaps are acceptable
///     (a rolled-back insert still consumes a number) but a value is never handed out twice, so the
///     generated value stays unique across processes and restarts — unlike a static in-process counter.
/// </summary>
/// <remarks>
///     ⚠️ <b>Handing values out was the only half that was concurrency-safe.</b> Creating the sequence
///     was not: <c>CREATE SEQUENCE IF NOT EXISTS</c> checks the catalogue against a snapshot and then
///     inserts, so two sessions reaching the <b>first</b> use at once both pass the check and the loser
///     raises a duplicate key on a system catalogue — which surfaced as the failure of the insert that
///     needed the number. The first use is exactly when an application starting under load asks.
///     It is handled in <c>EnsureSequenceAsync</c>: the error means the sequence exists, and
///     that is checked rather than assumed.
/// </remarks>
public static class SequenceValueProvider
{
    /// <summary>
    ///     Ensures the sequence exists and returns its next value for the current provider.
    /// </summary>
    /// <param name="dbContext">The boundary DbContext (its provider selects the dialect).</param>
    /// <param name="sequenceName">The sequence name (from entity metadata — never user input).</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<long> NextAsync(DbContext dbContext, string sequenceName, CancellationToken ct = default)
    {
        var provider = Detect(dbContext.Database.ProviderName);
        var quoted = Quote(sequenceName, provider);
        var literal = sequenceName.Replace("'", "''");

        // Go through raw ADO.NET rather than EF's SqlQueryRaw: fetching a sequence value is not a
        // model query, and routing it through EF's query pipeline makes the Npgsql provider try to
        // assign a type mapping to the wrapping expression and throw ("Expression '@p' ... does not
        // have a type mapping"). A DbCommand on the context's own connection sidesteps translation
        // entirely and enlists in any ambient transaction so it shares the caller's connection.
        var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var openedHere = false;
        if (dbContext.Database.GetDbConnection().State != ConnectionState.Open)
        {
            // Open through EF and not through the DbConnection itself: that path runs the registered
            // connection interceptors, and db-per-tenant routes a write by rewriting the connection
            // string in one of them (MT-H2). Opening the connection here skipped them, so the sequence
            // was created and read in the **shared** database while the row it numbers went to the
            // tenant's own — and two tenants asking at once raced on one CREATE SEQUENCE.
            await dbContext.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
            openedHere = true;
        }

        // After the open, because that is when the interceptors have had their say about where this
        // connection points.
        var connection = dbContext.Database.GetDbConnection();

        try
        {
            switch (provider)
            {
                case SqlProvider.PostgreSql:
                    // to_regclass resolves the name through the same search_path nextval will use, and
                    // answers null instead of raising for a name that is not there — so the check itself
                    // cannot abort the caller's transaction. ⚠️ relkind 'S' as well, because pg_class is
                    // one namespace for every relation: losing the race to a session creating a *table*
                    // of that name is the same 23505, and a check that asked to_regclass alone would
                    // read that table as the sequence and swallow it (measured: the caller then gets
                    // 42809 "is not a sequence" from the nextval below).
                    await EnsureSequenceAsync(
                        dbContext, connection, transaction,
                        create: $"CREATE SEQUENCE IF NOT EXISTS {quoted}",
                        exists: "SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_class "
                                + "WHERE oid = to_regclass(@name) AND relkind = 'S') THEN 1 ELSE 0 END",
                        existsArgument: quoted,
                        ct).ConfigureAwait(false);
                    // nextval() takes a regclass supplied as text; pass the double-quoted identifier so
                    // PostgreSQL preserves case instead of folding to lowercase. The sequence was created
                    // case-sensitively via {quoted}, so an unquoted 'Name' literal would look up the wrong
                    // (lower-cased) relation and raise 42P01.
                    return await ScalarAsync(connection, transaction,
                        $"SELECT nextval('{quoted.Replace("'", "''")}')", ct).ConfigureAwait(false);

                case SqlProvider.SqlServer:
                    // The sequence name is metadata (compile-time), not user input; dynamic SQL is safe here.
                    // The same shape and the same race — SQL Server announces it as 2714, "There is
                    // already an object named …". ⚠️ Unmeasured: the tests choreograph the collision on
                    // PostgreSQL, where the suite already has a container. What holds it
                    // together here is that nothing depends on the code: the create is attempted and
                    // sys.sequences is asked afterwards whether it happened.
                    await EnsureSequenceAsync(
                        dbContext, connection, transaction,
                        create: $"IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'{literal}') "
                                + $"EXEC('CREATE SEQUENCE {quoted} AS bigint START WITH 1 INCREMENT BY 1')",
                        exists: "SELECT CASE WHEN EXISTS "
                                + "(SELECT 1 FROM sys.sequences WHERE name = @name) THEN 1 ELSE 0 END",
                        existsArgument: sequenceName,
                        ct).ConfigureAwait(false);
                    return await ScalarAsync(connection, transaction, $"SELECT NEXT VALUE FOR {quoted}", ct)
                        .ConfigureAwait(false);

                case SqlProvider.Sqlite:
                    // SQLite has no sequences; emulate with a single-row counter table. SQLite serializes
                    // writers and UPDATE ... RETURNING (3.35+) is atomic, so the increment is race-free.
                    await ExecuteAsync(connection, transaction,
                        "CREATE TABLE IF NOT EXISTS __pragmatic_sequences (name TEXT PRIMARY KEY, value INTEGER NOT NULL)",
                        ct).ConfigureAwait(false);
                    await ExecuteAsync(connection, transaction,
                        "INSERT OR IGNORE INTO __pragmatic_sequences (name, value) VALUES (@name, 0)",
                        ct, ("@name", sequenceName)).ConfigureAwait(false);
                    return await ScalarAsync(connection, transaction,
                        "UPDATE __pragmatic_sequences SET value = value + 1 WHERE name = @name RETURNING value",
                        ct, ("@name", sequenceName)).ConfigureAwait(false);

                default:
                    throw new InvalidOperationException(
                        $"Unsupported database provider '{dbContext.Database.ProviderName}' for [GeneratedValue] {{SEQ}} sequences. " +
                        "Supported: PostgreSQL, SQL Server, SQLite.");
            }
        }
        finally
        {
            if (openedHere)
                await dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates the sequence if it is not there, and treats "it is already there" as the answer it
    ///     is instead of the failure of whatever needed the number.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>IF NOT EXISTS</c> is documented as <b>not free of race conditions</b> for the same
    ///         object: the check reads a snapshot and the insert happens after it, so two sessions on
    ///         the first use of one sequence both decide to create it. The loser raises a duplicate key
    ///         on a system catalogue — <c>23505</c> on <c>pg_class_relname_nsp_index</c>, or
    ///         <c>42P07</c>, or SQL Server's <c>2714</c> — and the number it was asked for never arrives.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>The post-condition is checked, not the error code.</b> Any failure of the create is
    ///         followed by asking whether the sequence exists now: if it does, somebody else created it
    ///         and there is nothing to report; if it does not, the original exception is rethrown
    ///         untouched, so a missing privilege or an unreachable server still fails and still says why.
    ///         Matching codes instead would tie this to a provider's exception type — which this
    ///         assembly deliberately does not reference — and would have to be right about every code
    ///         the race can produce.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>The savepoint is why this works inside a caller's transaction.</b> In PostgreSQL a
    ///         failed statement aborts the transaction it ran in, so swallowing the duplicate and
    ///         carrying on would leave the caller in <c>25P02</c> — "current transaction is aborted" —
    ///         and the insert that wanted the number would fail anyway, with a worse message than the
    ///         one it had. The create is attempted inside a savepoint whenever a transaction is ambient,
    ///         and rolling back to it undoes only the create. Nothing releases the savepoint: a commit
    ///         does, and SQL Server has no statement for it.
    ///     </para>
    /// </remarks>
    private static async Task EnsureSequenceAsync(
        DbContext dbContext, DbConnection connection, DbTransaction? transaction,
        string create, string exists, string existsArgument, CancellationToken ct)
    {
        // Asked first, so every call after the first costs the same two round trips it always did —
        // this one instead of the CREATE that had nothing to do — and the savepoint below is only ever
        // paid on the first use.
        if (await ExistsAsync(connection, transaction, exists, existsArgument, ct).ConfigureAwait(false))
            return;

        var ambient = dbContext.Database.CurrentTransaction;
        var savepoint = ambient is null ? null : "pragmatic_sequence_" + Guid.NewGuid().ToString("N");

        if (savepoint is not null)
            await ambient!.CreateSavepointAsync(savepoint, ct).ConfigureAwait(false);

        try
        {
            await ExecuteAsync(connection, transaction, create, ct).ConfigureAwait(false);
        }
        catch (DbException)
        {
            if (savepoint is not null)
                await ambient!.RollbackToSavepointAsync(savepoint, ct).ConfigureAwait(false);

            if (!await ExistsAsync(connection, transaction, exists, existsArgument, ct).ConfigureAwait(false))
                throw;
        }
    }

    private static async Task<bool> ExistsAsync(
        DbConnection connection, DbTransaction? transaction, string exists, string existsArgument,
        CancellationToken ct)
    {
        var present = await ScalarAsync(
            connection, transaction, exists, ct, ("@name", existsArgument)).ConfigureAwait(false);

        return present != 0;
    }

    private static async Task ExecuteAsync(
        DbConnection connection, DbTransaction? transaction, string sql, CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        using var command = CreateCommand(connection, transaction, sql, parameters);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<long> ScalarAsync(
        DbConnection connection, DbTransaction? transaction, string sql, CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        using var command = CreateCommand(connection, transaction, sql, parameters);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static DbCommand CreateCommand(
        DbConnection connection, DbTransaction? transaction, string sql, (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (transaction is not null)
            command.Transaction = transaction;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private enum SqlProvider { SqlServer, PostgreSql, Sqlite }

    private static SqlProvider Detect(string? providerName)
    {
        var name = providerName ?? string.Empty;
        if (name.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            return SqlProvider.SqlServer;
        if (name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            return SqlProvider.PostgreSql;
        if (name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            return SqlProvider.Sqlite;

        throw new InvalidOperationException(
            $"Unsupported database provider '{name}' for [GeneratedValue] {{SEQ}} sequences. " +
            "Supported: PostgreSQL, SQL Server, SQLite.");
    }

    private static string Quote(string identifier, SqlProvider provider)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.IndexOf('\0') >= 0)
            throw new ArgumentException("Sequence name must be a non-empty string without NUL.", nameof(identifier));

        return provider == SqlProvider.SqlServer
            ? $"[{identifier.Replace("]", "]]")}]"
            : $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
