using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql;

/// <summary>The .NET type a parameter carries, so the command can be typed once and prepared.</summary>
public enum SqlParamKind
{
    Guid,
    String,
    Int32,
    Int64,
    Decimal,
    Double,
    Boolean,
    DateTimeOffset,
    DateTime,
    Bytes,
    DateOnly,
    TimeOnly,
    /// <summary>A collection for an IN list: expanded to one parameter per item when the command is built, so the plan's text varies with the count.</summary>
    List,
    /// <summary>A <c>float[]</c> vector (<c>[Vector(n)]</c>): bound as its JSON text, the statement casts it to the dialect's vector type.</summary>
    Vector,
    /// <summary>A type the generator could not name: bound untyped, so the command is reused but not prepared.</summary>
    Unknown,
    /// <summary>An array column (<c>string[]</c>, <c>List    Unknown,lt;Guid    Unknown,gt;</c>…): a native array on PostgreSQL (bound untyped, the provider maps it), the JSON text elsewhere.</summary>
    Array,
}

/// <summary>Writes the parameter values of one call into the command: the arguments in the slots, the constants, and the paging.</summary>
public delegate void SqlBind<T>(in SqlQuery<T> query, DbParameterCollection parameters, SqlDialect dialect);

/// <summary>
///     Loads the collection includes from the result sets that follow the rows' own, in the same command: one
///     statement per collection, depth first, each selecting the children whose foreign key is in the parents'
///     keys (the parent statement as a subquery). One round trip whatever the depth.
/// </summary>
public delegate Task SqlLoad<T>(DbDataReader reader, SqlDialect dialect, List<T> rows, CancellationToken ct);

/// <summary>One id sequence for every plan of every row type: a static field of the generic class would be one per type, and two plans would share a cached command.</summary>
internal static class SqlPlanIds
{
    private static int s_next;

    public static int Next() => Interlocked.Increment(ref s_next) - 1;
}

/// <summary>
///     What the generator knows about one terminal call site: the SQL for each dialect (one statement, or one
///     per collection include after it), the parameters' types, how to bind them from a chain, how to read a
///     row. One instance per call site, created once; the session keeps a prepared command per plan, so a
///     call only binds values and executes.
/// </summary>
public sealed partial class SqlPlan<T> : ISqlPlan
{
    private readonly string _sqlite;
    private readonly string _postgreSql;
    private readonly string _sqlServer;

    private readonly string? _mySql; // null where the chain has no MySQL statement (RETURNING, a table-valued function): asked for, it says so
    private readonly string? _oracle; // null where the chain has no Oracle statement (RETURNING as rows)

    public SqlPlan(string sqlite, string postgreSql, string sqlServer, SqlParamKind[] parameters, SqlBind<T> bind, Func<DbDataReader, SqlDialect, T>? read, SqlLoad<T>? load = null, string? origin = null, bool reversed = false, SqlShapeColumn[]? shapeColumns = null, string? mySql = null, string? oracle = null, SqlPlanFilters? filters = null, int[]? predicates = null)
    {
        _filters = filters;
        _predicates = predicates;
        _sqlite = sqlite;
        _postgreSql = postgreSql;
        _sqlServer = sqlServer;
        _mySql = mySql;
        _oracle = oracle;
        Parameters = parameters;
        Bind = bind;
        Read = read;
        Load = load;
        Reversed = reversed;
        ShapeColumns = shapeColumns;
        Id = SqlPlanIds.Next();
        HasLists = Array.IndexOf(parameters, SqlParamKind.List) >= 0;
        // Not prepared ahead: a plan with IN lists or untyped parameters, and a SyncAsync plan, whose text takes the rows' VALUES per call ({rows}).
        Preparable = !HasLists && Array.IndexOf(parameters, SqlParamKind.Unknown) < 0 && !sqlite.Contains("{rows}", StringComparison.Ordinal);
        Origin = origin ?? "Plan_" + Id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Where the plan comes from (<c>Reads.cs(23,5) in Reads.RunAsync Plan_12</c>): the tag of its metrics and traces, the comment on its statements when the session annotates them.</summary>
    public string Origin { get; }

    /// <summary>
    ///     True for a <c>Before(cursor)</c> plan: the statement reads the rows nearest before the cursor in the ordering's reverse (so
    ///     a LIMIT takes the previous page), and the rows are put back in the chain's order once read. The first row of the
    ///     statement is the one just before the cursor (what <c>FirstOrDefaultAsync</c> answers); a stream buffers the rows.
    /// </summary>
    public bool Reversed { get; }

    /// <summary>The statement as the session runs it: with the origin as a leading comment when <see cref="SqlSession.AnnotateStatements" /> is on (what the database's log then shows).</summary>
    private string Text(SqlSession session, string sql) => session.AnnotateStatements ? "/* " + Origin + " */ " + sql : sql;

    private ConcurrentDictionary<(SqlDialect Dialect, string Schema), string>? _bySchema; // the text with the tables qualified, per schema it has run on

    /// <summary>
    ///     The statement for a session: the dialect's text, its tables qualified by the session's <see cref="SqlSession.Schema" /> (rewritten once
    ///     per schema, then the same string); for a filtered plan, the variant of the session's caller.
    /// </summary>
    public string Sql(SqlSession session) => _filters is null ? Qualified(session) : VariantFor(session, _filters.Mask(session)).Sql;

    /// <summary>The dialect's text with its tables qualified by the session's schema; a filtered plan's still marked.</summary>
    private string Qualified(SqlSession session)
    {
        if (session.Schema is not { } schema)
            return Raw(session.Dialect);
        var bySchema = _bySchema ??= new ConcurrentDictionary<(SqlDialect, string), string>();
        return bySchema.GetOrAdd((session.Dialect, schema), static (key, plan) => SqlTenantSchema.Qualify(plan.Raw(key.Dialect), key.Dialect, key.Schema), this);
    }

    /// <summary>
    ///     The statement as the session's database runs it, what <c>ToSql()</c> shows: <see cref="Sql(SqlSession)" />, except on Oracle, where the
    ///     <c>@pN</c> placeholders are <c>:pN</c> and a multi-statement text is the PL/SQL block (<see cref="SqlOracle.Text" />).
    /// </summary>
    public string RunnableSql(SqlSession session)
    {
        var sql = Sql(session);
        if (session.Dialect != SqlDialect.Oracle)
            return sql;
        var adapted = _oracleRunnable ??= new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        return adapted.GetOrAdd(sql, SqlOracle.Text); // once per text (one per schema): the same instance on every call, like the qualified text
    }

    private ConcurrentDictionary<string, string>? _oracleRunnable;

    /// <summary>Runs after the rows are read, on the same reader: the collection includes, from the result sets the statement's companions return.</summary>
    public SqlLoad<T>? Load { get; }

    /// <summary>The slot of this plan's command in every session's cache.</summary>
    public int Id { get; }

    public SqlParamKind[] Parameters { get; }

    public SqlBind<T> Bind { get; }

    /// <summary>Shapes a row; null for the scalar plans (count, any).</summary>
    public Func<DbDataReader, SqlDialect, T>? Read { get; }

    public bool HasLists { get; }

    public bool Preparable { get; }

    /// <summary>
    ///     The statement for a dialect; <c>@p0</c>… are the parameters, <c>{n}</c> stands where an IN list expands (SQLite, SQL Server); on PostgreSQL
    ///     a list is one array parameter (<c>= ANY(@pn)</c>). For a filtered plan, the variant with every filter active.
    /// </summary>
    public string Sql(SqlDialect dialect) => _filters is null ? Raw(dialect) : SqlPlanFilters.Apply(Raw(dialect), 0);

    private string Raw(SqlDialect dialect) => dialect switch
    {
        SqlDialect.Sqlite => _sqlite,
        SqlDialect.PostgreSql => HasLists ? ListPlaceholder.Replace(_postgreSql, "@p$1") : _postgreSql,
        SqlDialect.MySql => _mySql ?? throw new NotSupportedException($"Plan {Origin} has no MySQL statement: the chain uses a form MySQL has not (RETURNING/OUTPUT on a command, a table-valued function as the root); see the PRAG3012 note at the call site."),
        SqlDialect.Oracle => _oracle ?? throw new NotSupportedException($"Plan {Origin} has no Oracle statement: the chain uses a form Oracle has not (RETURNING rows from a command: Oracle's RETURNING … INTO fills variables, not a result set); see the PRAG3012 note at the call site."),
        _ => _sqlServer,
    };

    /// <summary>False where the plan has no statement for the dialect (MySQL: RETURNING/OUTPUT, a table-valued function; Oracle: RETURNING rows); <see cref="Sql(SqlDialect)" /> throws there.</summary>
    public bool Supports(SqlDialect dialect) => dialect switch { SqlDialect.MySql => _mySql is not null, SqlDialect.Oracle => _oracle is not null, _ => true };

    // Execution. The chain is passed by value: an async method cannot take it by reference. The terminals are ValueTasks
    // with the pooling builder: the state machine's box comes from a pool and no Task is allocated per call.

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<List<T>> ToListAsync(SqlQuery<T> query, CancellationToken ct)
    {
        var session = query.Session;
        var command = Command(query);
        var execution = SqlTelemetry.Begin(session, Origin, command);
        Exception? failure = null;
        try
        {
            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var list = new List<T>(query.TakeCount > 0 ? query.TakeCount : 16);
            var dialect = session.Dialect;
            var read = Read!;
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                list.Add(read(reader, dialect));
            if (Load is not null && list.Count > 0)
                await Load(reader, dialect, list, ct).ConfigureAwait(false); // the collections' result sets follow in the same command
            if (Reversed)
                list.Reverse(); // read nearest-first before the cursor; handed back in the chain's order
            return list;
        }
        catch (Exception e)
        {
            failure = e;
            throw;
        }
        finally
        {
            Release(command, execution, session, failure);
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T[]> ToArrayAsync(SqlQuery<T> query, CancellationToken ct) => (await ToListAsync(query, ct).ConfigureAwait(false)).ToArray();

    /// <summary>
    ///     The rows as they are read, one at a time: no list, whatever the count. The command's reader stays open until the
    ///     enumeration ends, so the session runs nothing else meanwhile. Not for a plan with collection includes (the loader needs the rows together).
    /// </summary>
    public async IAsyncEnumerable<T> Enumerate(SqlQuery<T> query, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (Reversed)
        {
            // The rows come nearest-first: the chain's order needs them all before the first is yielded.
            foreach (var row in await ToListAsync(query, ct).ConfigureAwait(false))
                yield return row;
            yield break;
        }

        var command = Command(query);
        var execution = SqlTelemetry.Begin(query.Session, Origin, command);
        try
        {
            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var dialect = query.Session.Dialect;
            var read = Read!;
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                yield return read(reader, dialect);
        }
        finally
        {
            Release(command, execution, query.Session, null); // an iterator cannot catch around a yield: a failure ends it without the hooks seeing the exception
        }
    }

    /// <summary>The rows keyed by a member, in memory.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Dictionary<TKey, T>> ToDictionaryAsync<TKey>(SqlQuery<T> query, Func<T, TKey> key, CancellationToken ct)
        where TKey : notnull
    {
        var rows = await ToListAsync(query, ct).ConfigureAwait(false);
        var dictionary = new Dictionary<TKey, T>(rows.Count);
        foreach (var row in rows)
            dictionary.Add(key(row), row);
        return dictionary;
    }

    /// <summary>A fresh command with the values bound, for inspection; never cached.</summary>
    public DbCommand ToCommand(in SqlQuery<T> query)
    {
        if (Dynamic)
            return DynamicToCommand(query);
        if (!Expands(query.Session.Dialect))
        {
            var plain = Create(query.Session, Sql(query.Session), query);
            Bind(query, plain.Parameters, query.Session.Dialect);
            return plain;
        }

        // An IN list: the values bound where a list may be held, the statement expanded from them, then the values on the provider's parameters.
        var probe = Probe(query);
        var command = Create(query.Session, ExpandLists(Sql(query.Session), probe), query);
        ExpandListValues(command, probe, query.Session.Dialect);
        if (query.Session.Dialect == SqlDialect.Oracle)
            SqlOracle.Adapt(command); // the expanded list's parameters too
        return command;
    }

    /// <summary>The prepared command of this plan in the session, values bound; a plan with IN lists builds one per call where the list expands.</summary>
    private DbCommand Command(in SqlQuery<T> query)
    {
        var session = query.Session;
        if (Dynamic)
            return DynamicCommand(query);
        if (Expands(session.Dialect))
            return ToCommand(query);
        var command = session.CommandFor(Id);
        if (command is null)
        {
            var sql = Sql(session);
            command = Create(session, sql, query);
            if (Preparable && session.Prepares(sql))
                command.Prepare();
            session.Keep(Id, command);
        }

        command.Transaction = session.Transaction;
        Bind(query, command.Parameters, session.Dialect);
        return command;
    }

    /// <summary>The plan's command created and prepared on the session ahead of its first call (<see cref="SqlSession.WarmupAsync" />); false when it is there already, or the plan cannot be prepared.</summary>
    public async Task<bool> WarmAsync(SqlSession session, CancellationToken ct)
    {
        if (Dynamic || !Preparable || !Supports(session.Dialect) || Expands(session.Dialect) || session.CommandFor(Id) is not null) // a filtered plan's variant depends on the caller
            return false;
        var sql = Sql(session);
        if (!session.Prepares(sql))
            return false;
        var command = Create(session, sql, default);
        await command.PrepareAsync(ct).ConfigureAwait(false);
        session.Keep(Id, command);
        return true;
    }

    private void Release(DbCommand command, in SqlExecution execution, SqlSession session, Exception? failure)
    {
        SqlTelemetry.End(execution, session, Origin, failure);
        if (Expands(session.Dialect))
            command.Dispose(); // built for this call; the session keeps the others
    }

    private DbCommand Create(SqlSession session, string sql, in SqlQuery<T> query)
    {
        var command = session.Connection.CreateCommand();
        command.CommandText = Text(session, sql);
        var dialect = session.Dialect;
        for (var i = 0; i < Parameters.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@p" + i.ToString(CultureInfo.InvariantCulture);
            SqlValue.Configure(parameter, Parameters[i], dialect);
            command.Parameters.Add(parameter);
        }

        if (session.CommandTimeout is { } timeout)
            command.CommandTimeout = timeout;
        if (dialect == SqlDialect.Oracle)
            SqlOracle.Adapt(command);
        session.ConfigureCommand?.Invoke(command);
        command.Transaction = session.Transaction;
        return command;
    }

}
