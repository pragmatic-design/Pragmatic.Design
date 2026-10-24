using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     An open connection and the dialect it speaks: what a repository holds for the duration of a
///     request. Queries start here (<see cref="Query{T}" />) and run here, each through the command its
///     plan prepared on this connection the first time; later calls only bind values.
/// </summary>
public sealed partial class SqlSession : IDisposable, IAsyncDisposable
{
    private DbCommand?[] _commands = [];
    private Dictionary<string, DbCommand>? _batches; // the batch commands, by the sequence of their plans

    // The C# compiler builds a predicate's expression tree without a MethodInfo for decimal operators (they are
    // predefined operators of the language, implemented as op_* methods of System.Decimal), so Expression.GreaterThan
    // and its siblings look the operator up by reflection when the tree is built. Native AOT trims those methods
    // unless something roots them: these attributes do, for the operators the translated subset accepts.
    [DynamicDependency("op_Equality", typeof(decimal))]
    [DynamicDependency("op_Inequality", typeof(decimal))]
    [DynamicDependency("op_LessThan", typeof(decimal))]
    [DynamicDependency("op_LessThanOrEqual", typeof(decimal))]
    [DynamicDependency("op_GreaterThan", typeof(decimal))]
    [DynamicDependency("op_GreaterThanOrEqual", typeof(decimal))]
    [DynamicDependency("op_Addition", typeof(decimal))]
    [DynamicDependency("op_Subtraction", typeof(decimal))]
    [DynamicDependency("op_Multiply", typeof(decimal))]
    [DynamicDependency("op_Division", typeof(decimal))]
    [DynamicDependency("op_Modulus", typeof(decimal))]
    [DynamicDependency("op_UnaryNegation", typeof(decimal))]
    public SqlSession(DbConnection connection, SqlDialect dialect)
    {
        Connection = connection;
        Dialect = dialect;
    }

    public DbConnection Connection { get; }

    public SqlDialect Dialect { get; }

    /// <summary>
    ///     The schema this session's tables live in (a tenant's): every statement runs with its tables qualified (<c>"tenant_a"."Orders"</c>),
    ///     the text rewritten once per plan and schema (see <see cref="SqlTenantSchema" />). Null: the tables as named, the connection's default schema.
    /// </summary>
    public string? Schema { get; init; }

    /// <summary>The tenant a pool rented this session for (<see cref="SqlSessionPool.RentAsync(string, CancellationToken)" />); null outside a tenant pool.</summary>
    public string? Tenant { get; internal set; }

    /// <summary>A statement's text as this session runs it: its tables qualified by <see cref="Schema" /> when there is one.</summary>
    public string Qualify(string sql) => Schema is null ? sql : SqlTenantSchema.Qualify(sql, Dialect, Schema);

    /// <summary>
    ///     Runs on every command when it is created: provider-specific settings this assembly does not know
    ///     (it references no provider). A prepared command excludes SqlClient's optimized parameter binding.
    /// </summary>
    public Action<DbCommand>? ConfigureCommand { get; init; }

    /// <summary>What runs before and after every statement this session executes (logging, timing, a circuit breaker); null costs nothing.</summary>
    public SqlExecutionHooks? Hooks { get; init; }

    /// <summary>The transaction every command runs in until it is committed or disposed; null outside one.</summary>
    public DbTransaction? Transaction { get; private set; }

    /// <summary>
    ///     True to prefix every plan's statement with its origin as a comment (<c>/* Reads.cs(23,5) in Reads.RunAsync Plan_12 */ SELECT …</c>):
    ///     the database's log and its statement statistics then name the call site. A few bytes per command; off by default.
    /// </summary>
    public bool AnnotateStatements { get; init; }

    /// <summary>The provider's bulk load (<see cref="ISqlBulkCopier" />), used by <see cref="InsertManyAsync{T}" /> from <see cref="BulkThreshold" /> rows on; null: multi-row INSERT statements whatever the count.</summary>
    public ISqlBulkCopier? BulkCopier { get; init; }

    /// <summary>
    ///     How many rows an <see cref="InsertManyAsync{T}" /> needs to go through <see cref="BulkCopier" />. Measured on the workload's Events (four
    ///     columns): at 1.000 rows the bulk load is 3,5× faster than the prepared lots of 256 on SQL Server and equal on PostgreSQL; at
    ///     10.000, 12× and 2×. Below, the lots are as fast and simpler.
    /// </summary>
    public int BulkThreshold { get; init; } = 1000;

    /// <summary>The entity's table as a query: every operator on it is a call site the generator translates.</summary>
    public SqlQuery<T> Query<T>() => new(this);

    /// <summary>
    ///     A statement written by hand as the root of a chain, its holes as parameters: <c>session.Sql&lt;Order&gt;($"SELECT * FROM \"Orders\" WHERE \"Total\" &gt; {minimum}")</c>.
    ///     Rows as the entity (the statement exposes its columns) or as a [MapFrom] DTO (its properties name the columns); the chain's operators apply on top.
    /// </summary>
    public SqlQuery<T> Sql<T>(ref SqlRawHandler statement) => new(this, statement.ToRaw());

    /// <summary>Several declared queries in one command: <c>var b = session.Batch(); var rows = b.OpenRows(id, 1, 20); var count = b.CountOpenAbove(1000m); await b.ExecuteAsync();</c>.</summary>
    public SqlBatch Batch() => new(this);

    /// <summary>
    ///     A common table expression as the root: <c>WITH cte AS (query) SELECT … FROM cte t</c>, the chain going on from it; with
    ///     <paramref name="materialized" /> the query is computed once (<c>AS MATERIALIZED</c> on PostgreSQL and SQLite, an optimization
    ///     fence; SQL Server has no such word, reported). Translated at compile time: the query is written in place.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Called as session.With(…) and intercepted at compile time: the call shape is the contract, a static would not bind.")]
    public SqlQuery<T> With<T>(SqlQuery<T> query, bool materialized = false) => query;

    /// <summary>
    ///     A recursive common table expression: <c>WITH RECURSIVE tree AS (anchor UNION ALL SELECT … FROM "T" t INNER JOIN tree ON step)
    ///     SELECT … FROM tree t</c>, where <paramref name="step" /> compares a row of the table (<c>t</c>) with a row already in the
    ///     tree (<c>tree</c>): <c>(c, tree) => c.ParentId == tree.Id</c> walks down, <c>(c, tree) => c.Id == tree.ParentId</c> walks up.
    ///     The chain goes on from the tree's rows. Translated at compile time.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Called as session.With(…) and intercepted at compile time: the call shape is the contract, a static would not bind.")]
    public SqlQuery<T> With<T>(SqlQuery<T> anchor, Func<T, T, bool> step, bool materialized = false) => anchor;

    /// <summary>
    ///     The recursive With bounded: at most <paramref name="maxDepth" /> steps from the anchor's rows (0: the anchor alone), a depth column
    ///     in the tree the step stops at (<c>WHERE tree.depth &lt; maxDepth</c>), on every dialect. A graph with a cycle cannot loop. A constant.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Called as session.With(…) and intercepted at compile time: the call shape is the contract, a static would not bind.")]
    public SqlQuery<T> With<T>(SqlQuery<T> anchor, Func<T, T, bool> step, int maxDepth, bool materialized = false) => anchor;

    /// <summary>The recursive With walked as <paramref name="walk" /> says (cycles stopped, depth- or breadth-first order), bounded when <paramref name="maxDepth" /> is 0 or more. Constants.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Called as session.With(…) and intercepted at compile time: the call shape is the contract, a static would not bind.")]
    public SqlQuery<T> With<T>(SqlQuery<T> anchor, Func<T, T, bool> step, SqlTreeWalk walk, int maxDepth = -1, bool materialized = false) => anchor;

    /// <summary>The prepared command of a batch shape (its plans in order), built the first time with <paramref name="build" /> and kept.</summary>
    internal DbCommand BatchCommand(string key, Func<SqlDialect, (string Sql, SqlParamKind[] Kinds)> build)
    {
        _batches ??= new Dictionary<string, DbCommand>(StringComparer.Ordinal);
        if (!_batches.TryGetValue(key, out var command))
        {
            var (sql, kinds) = build(Dialect);
            command = FreshCommand(sql, kinds);
            if (Array.IndexOf(kinds, SqlParamKind.Unknown) < 0 && Array.IndexOf(kinds, SqlParamKind.List) < 0 && (Dialect != SqlDialect.PostgreSql || Array.IndexOf(kinds, SqlParamKind.Array) < 0) && Prepares(sql))
                command.Prepare(); // a list (an array parameter on PostgreSQL) types itself from its value: not prepared ahead
            _batches[key] = command;
        }

        command.Transaction = Transaction;
        return command;
    }

    // Writes: explicit, no tracking. The statements come from the entity's registered writer.

    // Through the root of a table-per-concrete-type hierarchy (its map dispatches), each row goes to its own type's writer, in a write batch.

    public Task InsertAsync<T>(T row, CancellationToken ct = default) => Dispatches<T>() ? Through(b => b.Insert(row), ct) : Writer<T>().InsertAsync(this, row, ct);

    /// <summary>Inserts the rows in multi-row batches; returns how many the database counted. Its own name: an overload on IReadOnlyList would lose to InsertAsync&lt;List&lt;T&gt;&gt;.</summary>
    public Task<int> InsertManyAsync<T>(IReadOnlyList<T> rows, CancellationToken ct = default) => Dispatches<T>() ? ThroughMany(rows, static (b, r) => b.Insert(r), ct) : Writer<T>().InsertAsync(this, rows, ct);

    public Task UpdateAsync<T>(T row, CancellationToken ct = default) => Dispatches<T>() ? Through(b => b.Update(row), ct) : Writer<T>().UpdateAsync(this, row, ct);

    /// <summary>
    ///     Inserts the row or, when its key is already there, updates it (at the object's version, when it has one). Through the root of a
    ///     hierarchy the row's own type's writer; a row of a table-per-type hierarchy is two upserts, the root's table then the derived one's,
    ///     neither or both (in a transaction of their own, or a savepoint in the caller's).
    /// </summary>
    public async Task UpsertAsync<T>(T row, CancellationToken ct = default)
    {
        if (!Dispatches<T>() && Writer<T>() is { Part: null } plain)
        {
            await plain.UpsertAsync(this, row, ct).ConfigureAwait(false);
            return;
        }

        var writer = SqlWriteBatch.Writer(row);
        if (writer.Part is null)
        {
            await writer.UpsertAsync(this, row!, ct).ConfigureAwait(false);
            return;
        }

        var own = Transaction is null ? BeginTransaction() : null;
        var savepoint = own is null;
        if (savepoint)
            await SqlWriteBatch.SavepointAsync(this, rollback: false, ct).ConfigureAwait(false);
        try
        {
            foreach (var each in SqlWriteBatch.Chain(writer))
                await each.UpsertAsync(this, row!, ct).ConfigureAwait(false); // the root's table first: it holds the version, a stale row stops there
            own?.Commit();
        }
        catch when (savepoint)
        {
            await SqlWriteBatch.SavepointAsync(this, rollback: true, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            own?.Dispose();
        }
    }

    public Task DeleteAsync<T>(T row, CancellationToken ct = default) => Dispatches<T>() ? Through(b => b.Delete(row), ct) : Writer<T>().DeleteAsync(this, row, ct);

    /// <summary>Brings a soft-deleted row back (a [SoftDelete] entity): IsDeleted false, DeletedAt/By cleared, at its version.</summary>
    public Task RestoreAsync<T>(T row, CancellationToken ct = default) => Writer<T>().RestoreAsync(this, row, ct);

    /// <summary>
    ///     Writes what the entity says changed (<see cref="IChangeTracking" />, the repository's contract): a new row is inserted, a
    ///     modified one gets an UPDATE of its modified columns only (the version checked and advanced), an untouched one nothing; the
    ///     tracking is reset after. True when something was written.
    /// </summary>
    public async Task<bool> SaveAsync<T>(T row, CancellationToken ct = default) where T : class, IChangeTracking
    {
        if (!Dispatches<T>())
            return await Writer<T>().SaveAsync(this, row, ct).ConfigureAwait(false);
        var batch = WriteBatch().Save(row);
        await batch.ExecuteAsync(ct).ConfigureAwait(false);
        return batch.Count > 0;
    }

    /// <summary>The same for many rows, in as few round trips as their shapes allow (runs of the same columns, one command each); returns how many were written.</summary>
    public Task<int> SaveManyAsync<T>(IReadOnlyList<T> rows, CancellationToken ct = default) where T : class, IChangeTracking => Dispatches<T>() ? ThroughMany(rows, static (b, r) => b.Save(r), ct) : Writer<T>().SaveAsync(this, rows, ct);

    /// <summary>Links the two rows through a many-to-many side (<c>[ManyToMany]</c>, named by its collection property): one row of the join table.</summary>
    public Task LinkAsync<TParent, TChild>(TParent parent, string collection, TChild child, CancellationToken ct = default) => Through(b => b.Link(parent, collection, child), ct);

    /// <summary>Removes the link between the two rows (a conflict when it was not there).</summary>
    public Task UnlinkAsync<TParent, TChild>(TParent parent, string collection, TChild child, CancellationToken ct = default) => Through(b => b.Unlink(parent, collection, child), ct);

    private static bool Dispatches<T>() => SqlEntityMap<T>.Current.Dispatch;

    private async Task Through(Action<SqlWriteBatch> add, CancellationToken ct)
    {
        var batch = WriteBatch();
        add(batch);
        await batch.ExecuteAsync(ct).ConfigureAwait(false);
    }

    private async Task<int> ThroughMany<T>(IReadOnlyList<T> rows, Action<SqlWriteBatch, T> add, CancellationToken ct)
    {
        var batch = WriteBatch();
        foreach (var row in rows)
            add(batch, row);
        await batch.ExecuteAsync(ct).ConfigureAwait(false);
        return batch.Rows;
    }

    private static SqlWriter<T> Writer<T>() => SqlEntityMap<T>.Current.Writer ?? throw new InvalidOperationException(SqlEntityMap<T>.Current.ReadOnly
        ? $"{typeof(T).Name} is read-only ([Table(ReadOnly = true)], a view): it is read, never written."
        : SqlEntityMap<T>.Current.Dispatch
            ? $"{typeof(T).Name} is the root of a table-per-concrete-type hierarchy, a view over the types' tables: write through the row's own type (session.UpsertAsync<Derived>(row))."
            : $"No writer is registered for {typeof(T).Name}: the generator did not run on the assembly that declares it.");

    /// <summary>
    ///     A table-valued function as the root: <c>FROM name(@p0, @p1) t</c>, its rows read as <typeparamref name="T" /> (an entity, or a
    ///     <c>[MapFrom]</c> DTO), the chain going on from them (filters, navigations, joins, paging). The name is written as given, except
    ///     on SQL Server where a bare name gets the <c>dbo.</c> schema a function call needs. Translated at compile time: the name is a
    ///     constant, the arguments are parameters bound from this call.
    /// </summary>
    public SqlQuery<T> Function<T>(string name, params object?[] arguments) => new(this, new SqlRaw(name, arguments));

    /// <summary>Starts a transaction on the connection; every command of this session joins it until <see cref="DbTransaction.Commit" /> or dispose.</summary>
    public DbTransaction BeginTransaction() => BeginTransaction(System.Data.IsolationLevel.Unspecified);

    /// <summary>The same at an isolation level (the provider's default when unspecified; SQLite knows Serializable and ReadUncommitted). Savepoints through the transaction's <see cref="DbTransaction.Save" />, <c>Rollback(name)</c> and <see cref="DbTransaction.Release" />.</summary>
    public DbTransaction BeginTransaction(System.Data.IsolationLevel isolation)
    {
        if (Transaction is not null)
            throw new InvalidOperationException("A transaction is already open on this session.");
        Transaction = isolation == System.Data.IsolationLevel.Unspecified ? Connection.BeginTransaction() : Connection.BeginTransaction(isolation);
        return new SessionTransaction(this, Transaction);
    }

    /// <summary>Seconds every command of this session waits for the database, when set (the provider's default otherwise).</summary>
    public int? CommandTimeout { get; init; }

    /// <summary>The command this session prepared for a plan, or null the first time.</summary>
    public DbCommand? CommandFor(int planId) => planId < _commands.Length ? _commands[planId] : null;

    public void Keep(int planId, DbCommand command)
    {
        if (planId >= _commands.Length)
            Array.Resize(ref _commands, Math.Max(planId + 1, _commands.Length * 2));
        _commands[planId] = command;
    }

    /// <summary>The prepared command of a statement whose text and parameter kinds are known: created and prepared the first time, then reused.</summary>
    public DbCommand PreparedCommand(int planId, string sql, SqlParamKind[] kinds)
    {
        var command = CommandFor(planId);
        if (command is null)
        {
            command = Connection.CreateCommand();
            command.CommandText = Qualify(sql);
            var preparable = true;
            for (var i = 0; i < kinds.Length; i++)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                SqlValue.Configure(parameter, kinds[i], Dialect);
                preparable &= kinds[i] is not (SqlParamKind.Unknown or SqlParamKind.List) && (kinds[i] != SqlParamKind.Array || Dialect != SqlDialect.PostgreSql); // an array column on PostgreSQL types itself from its value
                command.Parameters.Add(parameter);
            }

            if (CommandTimeout is { } timeout)
                command.CommandTimeout = timeout;
            if (Dialect == SqlDialect.Oracle)
                SqlOracle.Adapt(command);
            ConfigureCommand?.Invoke(command);
            command.Transaction = Transaction;
            if (preparable && Prepares(sql))
                command.Prepare();
            Keep(planId, command);
        }

        command.Transaction = Transaction;
        return command;
    }

    /// <summary>
    ///     True when a statement may be prepared on the server: always, but for a text of several statements on MySQL, whose prepared
    ///     statements take one (the identity read-back, the versioned upsert and the sequences there run as text).
    /// </summary>
    internal bool Prepares(string sql) => Dialect != SqlDialect.MySql || !sql.Contains("; ", StringComparison.Ordinal);

    /// <summary>A command typed like a prepared one but not kept: for a statement shape too rare to cache (an unusual batch size).</summary>
    public DbCommand FreshCommand(string sql, SqlParamKind[] kinds)
    {
        var command = Connection.CreateCommand();
        command.CommandText = Qualify(sql);
        for (var i = 0; i < kinds.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            SqlValue.Configure(parameter, kinds[i], Dialect);
            command.Parameters.Add(parameter);
        }

        if (CommandTimeout is { } timeout)
            command.CommandTimeout = timeout;
        if (Dialect == SqlDialect.Oracle)
            SqlOracle.Adapt(command);
        ConfigureCommand?.Invoke(command);
        command.Transaction = Transaction;
        return command;
    }

    /// <summary>The session's transaction as the caller holds it: ending it detaches the session's commands.</summary>
    private sealed class SessionTransaction(SqlSession session, DbTransaction inner) : DbTransaction
    {
        protected override DbConnection DbConnection => inner.Connection!;

        public override System.Data.IsolationLevel IsolationLevel => inner.IsolationLevel;

        public override void Commit()
        {
            inner.Commit();
            session.Transaction = null;
        }

        public override void Rollback()
        {
            inner.Rollback();
            session.Transaction = null;
        }

        // Savepoints: the provider's, the session's commands stay in the transaction.
        public override bool SupportsSavepoints => inner.SupportsSavepoints;

        public override void Save(string savepointName) => inner.Save(savepointName);

        public override void Rollback(string savepointName) => inner.Rollback(savepointName);

        public override void Release(string savepointName) => inner.Release(savepointName);

        public override Task SaveAsync(string savepointName, CancellationToken cancellationToken = default) => inner.SaveAsync(savepointName, cancellationToken);

        public override Task RollbackAsync(string savepointName, CancellationToken cancellationToken = default) => inner.RollbackAsync(savepointName, cancellationToken);

        public override Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default) => inner.ReleaseAsync(savepointName, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                session.Transaction = null;
            }

            base.Dispose(disposing);
        }
    }

    public void Dispose()
    {
        DisposeCommands();
        Connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        DisposeCommands();
        await Connection.DisposeAsync().ConfigureAwait(false);
    }

    private void DisposeCommands()
    {
        foreach (var command in _commands)
            command?.Dispose();
        DisposePipelines();
        DisposeFilteredCommands();
        if (_batches is null)
            return;
        foreach (var command in _batches.Values)
            command.Dispose();
    }
}
