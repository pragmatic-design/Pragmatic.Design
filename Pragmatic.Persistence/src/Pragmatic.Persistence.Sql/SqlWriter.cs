using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>Another writer changed or removed the row since it was read: the update or delete matched nothing.</summary>
public sealed class SqlConcurrencyException : InvalidOperationException
{
    public SqlConcurrencyException(string table, object? key, object? version)
        : base($"The row of \"{table}\" with key {key} was not at version {version}: another writer changed or deleted it.")
    {
        Table = table;
        Key = key;
        Version = version;
    }

    /// <summary>A batch of statements wrote fewer rows than it sent: which rows were stale is not known.</summary>
    public SqlConcurrencyException(string table, int written, int sent)
        : base($"The batch on \"{table}\" wrote {written} of {sent} rows: another writer changed or deleted the others.")
    {
        Table = table;
    }

    public string Table { get; }

    public object? Key { get; }

    public object? Version { get; }
}

/// <summary>A key the database assigns (<c>[Identity]</c>): how a row binds without it, and how the value comes back into the object.</summary>
public sealed class SqlIdentity<T>(Action<T, DbParameterCollection, int, SqlDialect> bindWithoutKey, Action<T, DbDataReader, SqlDialect> readKey)
{
    public Action<T, DbParameterCollection, int, SqlDialect> BindWithoutKey { get; } = bindWithoutKey;

    public Action<T, DbDataReader, SqlDialect> ReadKey { get; } = readKey;
}

/// <summary>
///     The writes of one entity, generated in the assembly that declares it and registered with its map: the
///     columns in order, how a row's values are bound, the key and the optional version column. The
///     statements are built once per dialect (and per batch size for inserts) and run through the session's
///     prepared commands, like the plans of the reads. No change tracking: each call writes what it is given.
/// </summary>
public sealed partial class SqlWriter<T>
{
    private const int LargestBatch = 256;
    private const int CachedBatchSizes = 16; // prepared statements kept per entity for distinct batch sizes, beyond the single-row and 256-row ones

    private readonly string _table;
    private readonly string[] _columns;
    private readonly SqlParamKind[] _kinds;
    private readonly int[] _keyIndexes; // the key columns, by position; one for the usual Id, several for a composite key
    private readonly int _keyIndex; // the first key column: the one an identity assigns
    private readonly int _versionIndex;
    private readonly Action<T, DbParameterCollection, int, SqlDialect> _bindRow;
    private readonly Action<T, DbParameterCollection, int, SqlDialect> _bindKeys;
    private readonly Func<T, object?> _key;
    private readonly Func<T, object?>? _version;
    private readonly Action<T>? _bumpVersion;
    private readonly Action<T, DbDataReader, SqlDialect>? _readVersion;
    private readonly SqlIdentity<T>? _identity;
    private readonly string[] _insertColumns; // without the key when the database assigns it
    private readonly SqlParamKind[] _insertKinds;
    private readonly Action<T, DbParameterCollection, int, SqlDialect> _bindInsert;
    private readonly string?[]? _casts; // PostgreSQL: the type a column's parameter is cast to on writes (jsonb), by column
    private readonly ConcurrentDictionary<(SqlDialect, string), WritePlan> _plans = new();

    /// <summary>
    ///     Columns in the order the row binder writes them (one parameter per column, from an offset); the key columns and the version by
    ///     index; <paramref name="bindKeys" /> binds the key columns alone, in their order. <paramref name="properties" /> names the property
    ///     each column reads and <paramref name="bindColumn" /> binds one column at a parameter index: what a save of the modified columns needs.
    ///     <paramref name="columnValue" /> reads one column's value as it is bound: what a snapshot compares.
    /// </summary>
    public SqlWriter(string table, string[] columns, SqlParamKind[] kinds, int[] keyIndexes, int versionIndex, Action<T, DbParameterCollection, int, SqlDialect> bindRow, Action<T, DbParameterCollection, int, SqlDialect> bindKeys, Func<T, object?> key, Func<T, object?>? version, Action<T>? bumpVersion, Action<T, DbDataReader, SqlDialect>? readVersion = null, SqlIdentity<T>? identity = null, string?[]? casts = null, string[]? properties = null, Action<T, int, DbParameterCollection, int, SqlDialect>? bindColumn = null, SqlGenerated<T>? generated = null, SqlWriter<T>? part = null, Func<T, int, SqlDialect, object?>? columnValue = null)
    {
        _columnValue = columnValue;
        _table = table;
        _columns = columns;
        _kinds = kinds;
        _casts = casts;
        _properties = properties;
        _owners = properties is null ? null : Array.ConvertAll(properties, static p => p.IndexOf('.') is var dot && dot > 0 ? p.Substring(0, dot) : null);
        _bindColumn = bindColumn;
        _keyIndexes = keyIndexes;
        _keyIndex = keyIndexes.Length > 0 ? keyIndexes[0] : 0;
        _versionIndex = versionIndex;
        _bindRow = bindRow;
        _bindKeys = bindKeys;
        _key = key;
        _version = version;
        _bumpVersion = bumpVersion;
        _readVersion = readVersion;
        _identity = identity;
        _generated = generated;
        Part = part;
        if (identity is null)
        {
            _insertColumns = columns;
            _insertKinds = kinds;
            _bindInsert = bindRow;
        }
        else
        {
            _insertColumns = Without(columns, _keyIndex);
            _insertKinds = Without(kinds, _keyIndex);
            _bindInsert = identity.BindWithoutKey;
        }
    }

    /// <summary>The columns in the row binder's order, their kinds, and one row bound from an offset: what a statement built elsewhere over the entity's rows (a MERGE) needs.</summary>
    public string[] Columns => _columns;

    public SqlParamKind[] Kinds => _kinds;

    public void BindRow(T row, DbParameterCollection parameters, int offset, SqlDialect dialect) => _bindRow(row, parameters, offset, dialect);

    public async Task InsertAsync(SqlSession session, T row, CancellationToken ct)
    {
        RefuseNative(session.Dialect);
        if (HasRollUps(session))
        {
            using var own = session.BeginTransaction();
            await InsertAsync(session, row, ct).ConfigureAwait(false);
            own.Commit();
            return;
        }

        StampInsert(row);
        var plan = Plan(session.Dialect, "insert:1", d => Insert(d, 1), _insertKinds);
        var command = session.PreparedCommand(plan.Id, plan.Sql, plan.Kinds);
        _bindInsert(row, command.Parameters, 0, session.Dialect);
        if (_identity is null)
            await session.RunAsync(command, Origin("insert"), ct).ConfigureAwait(false);
        else
        {
            using var reader = await session.ReadAsync(command, Origin("insert"), ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
                _identity.ReadKey(row, reader, session.Dialect);
        }

        if (_generated is not null)
            await ReadGeneratedAsync(session, row, ct).ConfigureAwait(false);
        if (RollUps.Length > 0)
            await RollUpAsync(session, add: true, row, ct).ConfigureAwait(false);
    }

    /// <summary>The origin the session's hooks see for a write of this entity: the verb and the table.</summary>
    private string Origin(string verb) => verb + " \"" + _table + "\"";

    /// <summary>
    ///     One multi-row INSERT per 256 rows, the last with the exact remainder: one round trip (and one fsync) for a
    ///     batch of up to 256, in a transaction of its own when none is open. The statements of the first
    ///     <see cref="CachedBatchSizes" /> distinct sizes stay prepared in the session; a rarer size runs on a fresh command.
    ///     With an identity key, the keys come back in the order of the VALUES and land in the rows (ASSUMPTION the
    ///     equivalence tests check on every dialect: RETURNING and OUTPUT give one row per value list, in order).
    /// </summary>
    public async Task<int> InsertAsync(SqlSession session, IReadOnlyList<T> rows, CancellationToken ct)
    {
        RefuseNative(session.Dialect);
        if (rows.Count == 0)
            return 0;
        if (rows.Count == 1)
        {
            await InsertAsync(session, rows[0], ct).ConfigureAwait(false);
            return 1;
        }

        if (HasRollUps(session))
        {
            using var transaction = session.BeginTransaction();
            var inserted = await InsertAsync(session, rows, ct).ConfigureAwait(false);
            transaction.Commit();
            return inserted;
        }

        StampInsert(rows);
        if (Bulk(session, rows.Count, _identity))
        {
            var loaded = await BulkInsertAsync(session, rows, ct).ConfigureAwait(false); // the provider's bulk load, from the threshold on
            if (_generated is not null)
                await ReadGeneratedAsync(session, rows, 0, rows.Count, ct).ConfigureAwait(false);
            if (RollUps.Length > 0)
                await RollUpAsync(session, add: true, rows, 0, rows.Count, ct).ConfigureAwait(false);
            return loaded;
        }

        // One statement is atomic by itself; only a batch split over several needs a transaction of its own.
        var own = session.Transaction is null && rows.Count > LargestBatch ? session.BeginTransaction() : null;
        try
        {
            var written = 0;
            var start = 0;
            while (start < rows.Count)
            {
                var size = Math.Min(LargestBatch, rows.Count - start);
                var name = "insert:" + size.ToString(CultureInfo.InvariantCulture);
                var cached = _plans.ContainsKey((session.Dialect, name)) || _plans.Count < CachedBatchSizes;
                var plan = cached ? Plan(session.Dialect, name, d => Insert(d, size), Repeat(_insertKinds, size)) : new WritePlan(-1, Insert(session.Dialect, size), Repeat(_insertKinds, size));
                var command = cached ? session.PreparedCommand(plan.Id, plan.Sql, plan.Kinds) : session.FreshCommand(plan.Sql, plan.Kinds);
                try
                {
                    for (var i = 0; i < size; i++)
                        _bindInsert(rows[start + i], command.Parameters, i * _insertColumns.Length, session.Dialect);
                    if (_identity is null)
                        written += await session.RunAsync(command, Origin("insert"), ct).ConfigureAwait(false);
                    else
                    {
                        using var reader = await session.ReadAsync(command, Origin("insert"), ct).ConfigureAwait(false);
                        for (var i = 0; i < size && await reader.ReadAsync(ct).ConfigureAwait(false); i++)
                        {
                            _identity.ReadKey(rows[start + i], reader, session.Dialect);
                            written++;
                        }
                    }
                }
                finally
                {
                    if (!cached)
                        command.Dispose();
                }

                start += size;
            }

            if (_generated is not null)
                await ReadGeneratedAsync(session, rows, 0, rows.Count, ct).ConfigureAwait(false);
            if (RollUps.Length > 0)
                await RollUpAsync(session, add: true, rows, 0, rows.Count, ct).ConfigureAwait(false);
            own?.Commit();
            return written;
        }
        finally
        {
            own?.Dispose();
        }
    }

    /// <summary>Writes every non-key column; with a version column, only if the row is still at the object's version, which then advances.</summary>
    public async Task UpdateAsync(SqlSession session, T row, CancellationToken ct)
    {
        RefuseNative(session.Dialect);
        if (HasRollUps(session))
        {
            using var own = session.BeginTransaction();
            await UpdateAsync(session, row, ct).ConfigureAwait(false);
            own.Commit();
            return;
        }

        if (RollUps.Length > 0)
            await RollUpAsync(session, add: false, row, ct).ConfigureAwait(false); // the row's old contribution, before the update replaces it
        StampUpdate(row);
        var plan = Plan(session.Dialect, "update", Update, _kinds);
        var command = session.PreparedCommand(plan.Id, plan.Sql, plan.Kinds);
        // The row's values at their column positions (the key and the version among them, where the WHERE reads them).
        _bindRow(row, command.Parameters, 0, session.Dialect);
        var affected = await session.RunAsync(command, Origin("update"), ct).ConfigureAwait(false);
        if (affected == 0 && _versionIndex >= 0)
            throw new SqlConcurrencyException(_table, _key(row), _version!(row));
        _bumpVersion?.Invoke(row);
        if (_generated?.AfterUpdate == true)
            await ReadGeneratedAsync(session, row, ct).ConfigureAwait(false);
        if (RollUps.Length > 0)
            await RollUpAsync(session, add: true, row, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Inserts the row, or updates every non-key column when its key is there (<c>ON CONFLICT DO UPDATE</c>; <c>MERGE</c>
    ///     on SQL Server). With a version column the update applies only at the object's version, which then takes the
    ///     row's (read back); nothing written means another writer got there first. A database-assigned key has no
    ///     key to match on: refused.
    /// </summary>
    public async Task UpsertAsync(SqlSession session, T row, CancellationToken ct)
    {
        RefuseNative(session.Dialect);
        if (_identity is not null)
            throw new NotSupportedException($"\"{_table}\" has an [Identity] key: an upsert needs the key the row carries. Insert or update instead.");
        if (HasRollUps(session))
        {
            using var own = session.BeginTransaction();
            await UpsertAsync(session, row, ct).ConfigureAwait(false);
            own.Commit();
            return;
        }

        if (RollUps.Length > 0)
            await RollUpAsync(session, add: false, row, ct).ConfigureAwait(false); // the old contribution, when the row is there
        StampInsert(row); // the Created columns go in an insert only: an update keeps the stored ones
        var plan = Plan(session.Dialect, "upsert", Upsert, _kinds);
        var command = session.PreparedCommand(plan.Id, plan.Sql, plan.Kinds);
        _bindRow(row, command.Parameters, 0, session.Dialect);
        if (_versionIndex < 0)
        {
            await session.RunAsync(command, Origin("upsert"), ct).ConfigureAwait(false);
        }
        else
        {
            using var reader = await session.ReadAsync(command, Origin("upsert"), ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new SqlConcurrencyException(_table, _key(row), _version!(row));
            _readVersion!(row, reader, session.Dialect);
        }

        if (_generated is not null)
            await ReadGeneratedAsync(session, row, ct).ConfigureAwait(false); // inserted or updated: every generated column may have changed
        if (RollUps.Length > 0)
            await RollUpAsync(session, add: true, row, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(SqlSession session, T row, CancellationToken ct)
    {
        RefuseNative(session.Dialect);
        if (HasRollUps(session))
        {
            using var own = session.BeginTransaction();
            await DeleteAsync(session, row, ct).ConfigureAwait(false);
            own.Commit();
            return;
        }

        if (RollUps.Length > 0)
            await RollUpAsync(session, add: false, row, ct).ConfigureAwait(false); // a stale version undoes it with the transaction
        if (SoftDeletes)
        {
            await MarkDeletedAsync(session, row, deleted: true, ct).ConfigureAwait(false); // [SoftDelete]: the row stays, marked
            return;
        }

        var plan = Plan(session.Dialect, "delete", Delete, DeleteKinds());
        var command = session.PreparedCommand(plan.Id, plan.Sql, plan.Kinds);
        _bindKeys(row, command.Parameters, 0, session.Dialect);
        if (_versionIndex >= 0)
            BindVersion(row, command.Parameters, _keyIndexes.Length, session.Dialect);
        var affected = await session.RunAsync(command, Origin("delete"), ct).ConfigureAwait(false);
        if (affected == 0 && _versionIndex >= 0)
            throw new SqlConcurrencyException(_table, _key(row), _version!(row));
    }

    /// <summary>The statements, for inspection.</summary>
    public string InsertSql(SqlDialect dialect, int rows = 1) => Insert(dialect, rows);

    public string UpdateSql(SqlDialect dialect) => Update(dialect);

    public string UpsertSql(SqlDialect dialect) => Upsert(dialect);

    public string DeleteSql(SqlDialect dialect) => Delete(dialect);

    private WritePlan Plan(SqlDialect dialect, string name, Func<SqlDialect, string> sql, SqlParamKind[] kinds)
        => _plans.GetOrAdd((dialect, name), key => new WritePlan(SqlPlanIds.Next(), sql(key.Item1), kinds));

    // The update and the upsert bind every column (the version parameter is read by the WHERE only); the delete binds the key columns and the version.
    private SqlParamKind[] DeleteKinds()
    {
        var kinds = new SqlParamKind[_keyIndexes.Length + (_versionIndex >= 0 ? 1 : 0)];
        for (var k = 0; k < _keyIndexes.Length; k++)
            kinds[k] = _kinds[_keyIndexes[k]];
        if (_versionIndex >= 0)
            kinds[_keyIndexes.Length] = _kinds[_versionIndex];
        return kinds;
    }

    private static TItem[] Without<TItem>(TItem[] items, int index)
    {
        var result = new TItem[items.Length - 1];
        for (int i = 0, j = 0; i < items.Length; i++)
        {
            if (i != index)
                result[j++] = items[i];
        }

        return result;
    }

    private static SqlParamKind[] Repeat(SqlParamKind[] kinds, int times)
    {
        var all = new SqlParamKind[kinds.Length * times];
        for (var i = 0; i < times; i++)
            kinds.CopyTo(all, i * kinds.Length);
        return all;
    }

    private sealed class WritePlan(int id, string sql, SqlParamKind[] kinds)
    {
        public int Id { get; } = id;

        public string Sql { get; } = sql;

        public SqlParamKind[] Kinds { get; } = kinds;
    }
}
