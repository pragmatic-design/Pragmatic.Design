using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     Several writes in one command: inserts, saves of the modified columns, updates, deletes, and the orphan deletes of a graph,
///     their statements joined with the parameters renumbered, in a transaction of their own when none is open and there is more
///     than one. Every statement reports its own count as a result set (<c>RETURNING 1</c> on SQLite and PostgreSQL, <c>SELECT
///     @@ROWCOUNT</c> on SQL Server; an identity insert's own key rows), so a version is checked row by row and the exception names the
///     stale one. The command is prepared once per shape and kept with the session, unless a statement's shape varies per call
///     (an orphan delete's key list).
/// </summary>
public sealed partial class SqlWriteBatch
{
    private static readonly Regex ParameterName = new(@"@p(\d+)", RegexOptions.Compiled);
    private const string Savepoint = "pragmatic_write_batch";
    private readonly List<SqlWriteItem> _items = [];

    internal SqlWriteBatch(SqlSession session) => Session = session;

    public SqlSession Session { get; }

    /// <summary>The statements recorded so far.</summary>
    public int Count => _items.Count;

    /// <summary>The rows written so far (a row in a table per type is two statements: one row).</summary>
    internal int Rows { get; private set; }

    public SqlWriteBatch Insert<T>(T row)
    {
        var writer = Writer(row);
        AddWrite(writer, row!, writer.InsertItem(row!, Session.Dialect), before: false, after: true);
        for (var part = writer.Part; part is not null; part = part.Part)
            AddWrite(part, row!, part.InsertItem(row!, Session.Dialect), before: false, after: true); // each level's table after the one above (a foreign key may tie them)
        Rows++;
        return this;
    }

    /// <summary>The row's modified columns, or its insert when it is new; nothing when it is untouched.</summary>
    public SqlWriteBatch Save<T>(T row) where T : class, IChangeTracking
    {
        var writer = Writer(row);
        var written = false;
        foreach (var each in Chain(writer))
        {
            var item = row.IsNew ? each.InsertItem(row, Session.Dialect) : each.SaveItem(row, Session.Dialect); // each table its own modified columns
            if (item is null)
                continue;
            var moves = !row.IsNew && each.SaveMovesRollUps(row);
            AddWrite(each, row, item, before: moves, after: row.IsNew || moves);
            written = true;
        }

        if (written)
            Rows++;
        return this;
    }

    public SqlWriteBatch Update<T>(T row)
    {
        var writer = Writer(row);
        AddWrite(writer, row!, writer.UpdateItem(row!, Session.Dialect), before: true, after: true);
        for (var part = writer.Part; part is not null; part = part.Part)
            AddWrite(part, row!, part.UpdateItem(row!, Session.Dialect), before: true, after: true);
        Rows++;
        return this;
    }

    public SqlWriteBatch Delete<T>(T row)
    {
        var chain = Chain(Writer(row));
        if (chain[0].SoftDeletes)
        {
            AddWrite(chain[0], row!, chain[0].DeleteItem(row!, Session.Dialect), before: true, after: false); // [SoftDelete]: the root's row marked, the parts kept
            Rows++;
            return this;
        }

        for (var i = chain.Count - 1; i >= 0; i--)
            AddWrite(chain[i], row!, chain[i].DeleteItem(row!, Session.Dialect), before: true, after: false); // the deepest table's row first, the root's last (a foreign key may tie them)
        Rows++;
        return this;
    }

    /// <summary>One link of a many-to-many side (<c>[ManyToMany]</c>, named by its collection property) between the two rows: a row of the join table.</summary>
    public SqlWriteBatch Link<TParent, TChild>(TParent parent, string collection, TChild child)
    {
        var (map, link, childMap) = LinkOf<TParent, TChild>(collection);
        var (parentKinds, parentKey) = SqlSession.KeyValues(map, parent!);
        var (childKinds, childKey) = SqlSession.KeyValues(childMap, child!);
        _items.Add(SqlLinkItems.Insert(link, parentKinds, parentKey, childKinds, childKey));
        return this;
    }

    /// <summary>The link between the two rows removed (one row of the join table; a conflict when it was not there).</summary>
    public SqlWriteBatch Unlink<TParent, TChild>(TParent parent, string collection, TChild child)
    {
        var (map, link, childMap) = LinkOf<TParent, TChild>(collection);
        var (parentKinds, parentKey) = SqlSession.KeyValues(map, parent!);
        var (childKinds, childKey) = SqlSession.KeyValues(childMap, child!);
        _items.Add(SqlLinkItems.Delete(link, parentKinds, parentKey, childKinds, childKey));
        return this;
    }

    private static (SqlEntityMap Map, SqlCollection Link, SqlEntityMap ChildMap) LinkOf<TParent, TChild>(string collection)
    {
        var map = SqlEntityMap<TParent>.Current;
        var link = map.Collection(collection) ?? throw new ArgumentException($"'{collection}' is not a collection of {typeof(TParent).Name}.", nameof(collection));
        if (!link.IsLink)
            throw new ArgumentException($"{typeof(TParent).Name}.{collection} is a one-to-many: its rows carry the parent's key, write them (Insert, Delete, SaveGraphAsync); Link and Unlink are for a [ManyToMany] side.", nameof(collection));
        if (link.Child != typeof(TChild))
            throw new ArgumentException($"{typeof(TParent).Name}.{collection} holds {link.Child.Name}, not {typeof(TChild).Name}.", nameof(collection));
        return (map, link, SqlEntityMap<TChild>.Current);
    }

    internal void Add(SqlWriteItem item) => _items.Add(item);

    /// <summary>A row's write with the roll-ups it moves (<see cref="RollUpAttribute{TChild}" />): its old contribution subtracted before, its new one added after.</summary>
    internal void AddWrite(ISqlWriter writer, object row, SqlWriteItem item, bool before, bool after)
    {
        if (before)
            _items.AddRange(writer.RollUpItems(row, add: false, Session.Dialect));
        _items.Add(item);
        if (after)
            _items.AddRange(writer.RollUpItems(row, add: true, Session.Dialect));
    }

    /// <summary>Runs the recorded statements in one command, checks every count, then lets each item finish its row (version advanced, tracking reset, identity key read).</summary>
    public async ValueTask ExecuteAsync(CancellationToken ct = default)
    {
        if (_items.Count == 0)
            return;
        var dialect = Session.Dialect;
        var variable = false;
        foreach (var item in _items)
            variable |= item.Shape < 0;
        var own = Session.Transaction is null && _items.Count > 1 ? Session.BeginTransaction() : null;
        // Inside the caller's transaction a failed statement (a stale version) must not leave the ones before it applied: a savepoint the batch
        // rolls back to, as EF Core's SaveChanges does (a row of a table-per-type hierarchy is two statements: neither or both).
        // The savepoint in SQL: SqlClient and MySqlConnector support savepoints but report SupportsSavepoints false.
        var savepoint = own is null && _items.Count > 1 && Session.Transaction is not null;
        if (savepoint)
            await SavepointAsync(Session, rollback: false, ct).ConfigureAwait(false);
        DbCommand? fresh = null;
        DbBatch? freshPipeline = null;
        try
        {
            // Pipelined: the provider's batch, one command per statement (its count form appended), kept per shape or built for this call when a statement's text varies.
            DbBatch? pipeline = null;
            if (Session.Pipelining)
            {
                if (variable)
                    pipeline = freshPipeline = Session.FreshPipeline(Commands(dialect), out _);
                else
                    pipeline = Session.PipelineBatch(Key(), Commands);
            }

            DbDataReader reader;
            if (pipeline is not null)
            {
                for (var i = 0; i < _items.Count; i++)
                    _items[i].Bind(pipeline.BatchCommands[i].Parameters, 0, dialect);
                reader = await Session.ReadAsync(pipeline, "write batch", ct).ConfigureAwait(false);
            }
            else
            {
                DbCommand command;
                if (variable)
                {
                    var (sql, kinds) = Build(dialect);
                    command = fresh = Session.FreshCommand(sql, kinds);
                }
                else
                    command = Session.BatchCommand(Key(), Build);

                var offset = 0;
                foreach (var item in _items)
                {
                    item.Bind(command.Parameters, offset, dialect);
                    offset += item.Kinds.Length;
                }

                reader = await Session.ReadAsync(command, "write batch", ct).ConfigureAwait(false);
            }

            using (reader)
            {
                for (var i = 0; i < _items.Count; i++)
                {
                    if (i > 0 && !await reader.NextResultAsync(ct).ConfigureAwait(false))
                        throw new InvalidOperationException($"The write batch ran {_items.Count} statements but came back with {i} result sets: a statement reported no count.");
                    var item = _items[i];
                    int count;
                    if (dialect is SqlDialect.SqlServer or SqlDialect.MySql or SqlDialect.Oracle && !item.ReturnsRows)
                        count = await reader.ReadAsync(ct).ConfigureAwait(false) ? SqlValue.Count(reader.GetValue(0)) : 0;
                    else
                    {
                        count = 0;
                        while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        {
                            item.Row?.Invoke(reader, dialect);
                            count++;
                        }
                    }

                    if (item.Expected >= 0 && count < item.Expected)
                        throw item.Conflict?.Invoke(count) ?? new InvalidOperationException($"A statement on \"{item.Table}\" touched {count} rows, not {item.Expected}.");
                }
            }

            foreach (var item in _items)
                item.Completed?.Invoke();
            Dictionary<ISqlWriter, List<object>>? readBack = null;
            foreach (var item in _items)
            {
                if (item.ReadBackBy is { } by)
                {
                    readBack ??= [];
                    if (!readBack.TryGetValue(by, out var rows))
                        readBack[by] = rows = [];
                    rows.Add(item.ReadBackRow!);
                }
            }

            if (readBack is not null)
            {
                foreach (var (by, rows) in readBack)
                    await by.ReadGeneratedAsync(Session, rows, ct).ConfigureAwait(false); // per writer, in lots, after the command (its reader closed), in the batch's transaction
            }

            own?.Commit();
        }
        catch when (savepoint)
        {
            await SavepointAsync(Session, rollback: true, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            own?.Dispose();
            fresh?.Dispose();
            freshPipeline?.Dispose();
        }
    }

    /// <summary>The batch's savepoint in the caller's transaction, set or rolled back to (SAVE / ROLLBACK TRANSACTION on SQL Server); a table-per-type upsert's too.</summary>
    internal static async Task SavepointAsync(SqlSession session, bool rollback, CancellationToken ct)
    {
        var sql = (session.Dialect, rollback) switch
        {
            (SqlDialect.SqlServer, false) => "SAVE TRANSACTION " + Savepoint,
            (SqlDialect.SqlServer, true) => "ROLLBACK TRANSACTION " + Savepoint,
            (_, false) => "SAVEPOINT " + Savepoint,
            _ => "ROLLBACK TO SAVEPOINT " + Savepoint,
        };
        using var command = session.FreshCommand(sql, []);
        await session.RunAsync(command, rollback ? "write batch rollback" : "write batch savepoint", ct).ConfigureAwait(false);
    }

    /// <summary>The statements as the provider's batch takes them: one text each, its count form appended, its parameters from <c>@p0</c>.</summary>
    private List<(string Sql, SqlParamKind[] Kinds)> Commands(SqlDialect dialect)
    {
        var commands = new List<(string, SqlParamKind[])>(_items.Count);
        foreach (var item in _items)
        {
            var text = item.Sql(dialect);
            if (!item.ReturnsRows)
                text = CountForm(text, dialect);
            commands.Add((text, item.Kinds));
        }

        return commands;
    }

    /// <summary>A statement with its row count as a result set: <c>RETURNING 1</c> (one row per row touched) where the dialect has it, the count itself after the statement on SQL Server and MySQL.</summary>
    internal static string CountForm(string text, SqlDialect dialect) => dialect switch
    {
        SqlDialect.SqlServer => text + "; SELECT @@ROWCOUNT",
        SqlDialect.MySql or SqlDialect.Oracle => text + "; SELECT ROW_COUNT()", // Oracle: the PL/SQL block's count marker (SqlOracle)
        _ => text + " RETURNING 1",
    };

    /// <summary>The command text on the session's dialect, for inspection.</summary>
    public string ToSql() => Session.Dialect == SqlDialect.Oracle ? SqlOracle.Text(Build(Session.Dialect).Sql) : Build(Session.Dialect).Sql; // as the database runs it: Oracle's is the PL/SQL block

    /// <summary>The entity's writer, or the row's own type's when the entity is the root of a table-per-concrete-type hierarchy.</summary>
    /// <summary>The writer and its parts, the root's table first (a table per type: one per level).</summary>
    internal static List<ISqlWriter> Chain(ISqlWriter writer)
    {
        var chain = new List<ISqlWriter>(2);
        for (ISqlWriter? each = writer; each is not null; each = each.Part)
            chain.Add(each);
        return chain;
    }

    /// <summary>The writer of the row's own type (through the root of a hierarchy, the derived type's).</summary>
    internal static ISqlWriter Writer<T>(T row) => SqlEntityMap<T>.Current.WriterFor(row ?? throw new ArgumentNullException(nameof(row)));

    private string Key()
    {
        var key = new StringBuilder(_items.Count * 4 + 1).Append('w');
        foreach (var item in _items)
            key.Append(item.Shape).Append(',');
        return key.ToString();
    }

    /// <summary>The statements joined, each with its count form and its parameters renumbered after the ones before.</summary>
    private (string Sql, SqlParamKind[] Kinds) Build(SqlDialect dialect)
    {
        var sql = new StringBuilder();
        var kinds = new List<SqlParamKind>();
        var offset = 0;
        foreach (var item in _items)
        {
            if (sql.Length > 0)
                sql.Append("; ");
            var shift = offset;
            var text = item.Sql(dialect);
            if (!item.ReturnsRows)
                text = CountForm(text, dialect);
            sql.Append(ParameterName.Replace(text, m => "@p" + (shift + int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture)));
            kinds.AddRange(item.Kinds);
            offset += item.Kinds.Length;
        }

        return (sql.ToString(), kinds.ToArray());
    }
}

/// <summary>
///     One statement of a write batch: its text per dialect (without the count form, which the batch adds), its parameter kinds and
///     binder, how many rows it must touch (-1 when any number will do), whether its own rows come back (an identity insert, read
///     by <see cref="Row" />), what to do once the batch succeeded, and the exception a short count means.
/// </summary>
internal sealed class SqlWriteItem(string table, int shape, SqlParamKind[] kinds, Func<SqlDialect, string> sql, Action<DbParameterCollection, int, SqlDialect> bind, int expected)
{
    public string Table { get; } = table;

    /// <summary>The statement's prepared-plan id, for the batch's shape key; -1 when the text varies per call (the command is not kept).</summary>
    public int Shape { get; } = shape;

    public SqlParamKind[] Kinds { get; } = kinds;

    public int Expected { get; } = expected;

    public bool ReturnsRows { get; init; }

    public Action<DbDataReader, SqlDialect>? Row { get; init; }

    public Action? Completed { get; init; }

    public Func<int, SqlConcurrencyException>? Conflict { get; init; }

    /// <summary>The writer that reads the row's database-filled columns (<c>[DatabaseGenerated]</c>) back after the batch's command; null when there are none to read.</summary>
    public ISqlWriter? ReadBackBy { get; init; }

    /// <summary>The row <see cref="ReadBackBy" /> reads back into.</summary>
    public object? ReadBackRow { get; init; }

    public string Sql(SqlDialect dialect) => sql(dialect);

    public void Bind(DbParameterCollection parameters, int offset, SqlDialect dialect) => bind(parameters, offset, dialect);
}
