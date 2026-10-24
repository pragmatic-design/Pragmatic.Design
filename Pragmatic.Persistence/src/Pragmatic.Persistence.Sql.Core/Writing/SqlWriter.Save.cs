using System.Buffers;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>
///     Writes driven by the entity's own change tracking (<see cref="IChangeTracking" />, the repository's contract): a new row is
///     inserted, a modified one gets an UPDATE of its modified columns only (one statement per set of columns, prepared once and
///     kept; the values bound by the generated per-column binder), an untouched one nothing; the version checked and advanced,
///     the tracking reset after. Many rows go in runs of the same columns: one multi-statement command per run of up to 256 rows,
///     within the dialect's parameter budget, in a transaction of their own when it takes more than one command.
/// </summary>
public sealed partial class SqlWriter<T>
{
    private const int SqlServerParameterBudget = 2000; // 2100 per command there; the other dialects allow tens of thousands
    private const int ParameterBudget = 30000;
    private const int CachedSavePlans = 64; // distinct (columns, rows) shapes kept prepared per entity; a rarer one runs on a fresh command

    private readonly string[]? _properties; // the property each column reads, parallel to _columns (Owner.Member for an owned type's member)
    private readonly string?[]? _owners; // the owner of an owned type's member, parallel to _columns: a replaced owner modifies its members
    private readonly Action<T, int, DbParameterCollection, int, SqlDialect>? _bindColumn;
    private readonly ConcurrentDictionary<(SqlDialect Dialect, ulong Mask, int Rows), WritePlan> _savePlans = new();

    /// <summary>Inserts the row when it is new, updates its modified columns when it has any, does nothing otherwise; resets the tracking. True when something was written.</summary>
    public async Task<bool> SaveAsync<TTracked>(SqlSession session, TTracked row, CancellationToken ct) where TTracked : class, T, IChangeTracking
    {
        RefuseNative(session.Dialect);
        EnsureSaves();
        if (row.IsNew)
        {
            await InsertAsync(session, row, ct).ConfigureAwait(false);
            Saved(row);
            return true;
        }

        var mask = MaskOf(row.ModifiedProperties);
        if (mask == 0)
            return false;
        var moves = MovesRollUps(mask);
        if (moves && session.Transaction is null)
        {
            using var own = session.BeginTransaction();
            await SaveAsync(session, row, ct).ConfigureAwait(false);
            own.Commit();
            return true;
        }

        if (moves)
            await RollUpAsync(session, add: false, row, ct).ConfigureAwait(false);
        StampUpdate(row);
        var plan = SavePlan(session.Dialect, mask, 1, out var cached);
        var command = cached ? session.PreparedCommand(plan.Id, plan.Sql, plan.Kinds) : session.FreshCommand(plan.Sql, plan.Kinds);
        try
        {
            BindSave(row, mask, command.Parameters, 0, session.Dialect);
            var affected = await session.RunAsync(command, Origin("save"), ct).ConfigureAwait(false);
            if (affected == 0 && _versionIndex >= 0)
                throw new SqlConcurrencyException(_table, _key(row), _version!(row));
        }
        finally
        {
            if (!cached)
                command.Dispose();
        }

        _bumpVersion?.Invoke(row);
        if (_generated?.AfterUpdate == true)
            await ReadGeneratedAsync(session, row, ct).ConfigureAwait(false);
        if (moves)
            await RollUpAsync(session, add: true, row, ct).ConfigureAwait(false);
        Saved(row);
        return true;
    }

    /// <summary>
    ///     Saves the rows in as few round trips as their shapes allow: the new ones inserted in batches, the modified ones in runs of
    ///     the same columns (one command per run, up to 256 rows and within the parameter budget; one row per command on SQLite), the
    ///     untouched ones skipped; a transaction of its own around more than one row. With a version column every statement checks
    ///     it: fewer rows affected than sent is a conflict (which rows is not known: the exception says how many), and the
    ///     transaction undoes the batch. Returns how many rows were written.
    /// </summary>
    public async Task<int> SaveAsync<TTracked>(SqlSession session, IReadOnlyList<TTracked> rows, CancellationToken ct) where TTracked : class, T, IChangeTracking
    {
        RefuseNative(session.Dialect);
        EnsureSaves();
        if (rows.Count == 0)
            return 0;
        if (rows.Count == 1)
            return await SaveAsync(session, rows[0], ct).ConfigureAwait(false) ? 1 : 0;

        // The shape of the work first (the new rows, each row's mask), to know how many commands it takes: an untouched row breaks a run, as the second pass sees it.
        var budget = session.Dialect == SqlDialect.SqlServer ? SqlServerParameterBudget : ParameterBudget;
        var masks = ArrayPool<ulong>.Shared.Rent(rows.Count);
        try
        {
            List<TTracked>? fresh = null;
            var commands = 0;
            var writes = 0;
            ulong previous = 0;
            var inRun = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                masks[i] = 0;
                if (row.IsNew)
                {
                    (fresh ??= []).Add(row);
                    writes++;
                    previous = 0;
                    continue;
                }

                var mask = MaskOf(row.ModifiedProperties);
                masks[i] = mask;
                if (mask == 0)
                {
                    previous = 0;
                    continue;
                }

                writes++;
                if (mask != previous || inRun == RunSize(session.Dialect, mask, budget))
                {
                    commands++;
                    inRun = 0;
                    previous = mask;
                }

                inRun++;
            }

            if (fresh is not null)
                commands += (fresh.Count + LargestBatch - 1) / LargestBatch;
            // More than one row to write: a transaction of its own. Without one every statement commits by itself (a multi-statement command is not
            // atomic: SQLite and SQL Server flush the log per statement, measured at 10-100× the time), and a stale row could not undo the others.
            var own = session.Transaction is null && (commands > 1 || writes > 1 || (writes > 0 && RollUps.Length > 0)) ? session.BeginTransaction() : null;
            try
            {
                var written = 0;
                if (fresh is not null)
                {
                    written += await InsertAsync(session, fresh, ct).ConfigureAwait(false);
                    foreach (var row in fresh)
                        Saved(row);
                }

                var start = 0;
                while (start < rows.Count)
                {
                    var mask = masks[start];
                    if (mask == 0)
                    {
                        start++;
                        continue;
                    }

                    var limit = RunSize(session.Dialect, mask, budget);
                    var size = 1;
                    while (size < limit && start + size < rows.Count && masks[start + size] == mask)
                        size++;
                    written += await SaveRunAsync(session, rows, start, size, mask, ct).ConfigureAwait(false);
                    start += size;
                }

                own?.Commit();
                return written;
            }
            finally
            {
                own?.Dispose();
            }
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(masks);
        }
    }

    /// <summary>The statement a save of these modified properties runs, for inspection (<paramref name="rows" /> of them in one command).</summary>
    public string SaveSql(SqlDialect dialect, IReadOnlySet<string> modified, int rows = 1)
    {
        EnsureSaves();
        return Save(dialect, MaskOf(modified), rows);
    }

    private async Task<int> SaveRunAsync<TTracked>(SqlSession session, IReadOnlyList<TTracked> rows, int start, int size, ulong mask, CancellationToken ct) where TTracked : class, T, IChangeTracking
    {
        var moves = MovesRollUps(mask);
        var run = new T[size]; // the run's rows as the writer's type, for one read back or roll-up of the lot
        for (var i = 0; i < size; i++)
            run[i] = rows[start + i];
        if (moves)
            await RollUpAsync(session, add: false, run, 0, size, ct).ConfigureAwait(false);
        foreach (var row in run)
            StampUpdate(row);
        var plan = SavePlan(session.Dialect, mask, size, out var cached);
        var command = cached ? session.PreparedCommand(plan.Id, plan.Sql, plan.Kinds) : session.FreshCommand(plan.Sql, plan.Kinds);
        try
        {
            var per = ParametersPerRow(mask);
            for (var i = 0; i < size; i++)
                BindSave(rows[start + i], mask, command.Parameters, i * per, session.Dialect);
            var affected = await session.RunAsync(command, Origin("save"), ct).ConfigureAwait(false);
            if (affected < size && _versionIndex >= 0)
                throw size == 1 ? new SqlConcurrencyException(_table, _key(rows[start]), _version!(rows[start])) : new SqlConcurrencyException(_table, affected, size);
        }
        finally
        {
            if (!cached)
                command.Dispose();
        }

        if (_generated?.AfterUpdate == true)
            await ReadGeneratedAsync(session, run, 0, size, ct).ConfigureAwait(false);
        if (moves)
            await RollUpAsync(session, add: true, run, 0, size, ct).ConfigureAwait(false);

        for (var i = 0; i < size; i++)
        {
            var row = rows[start + i];
            _bumpVersion?.Invoke(row);
            Saved(row);
        }

        return size;
    }

    private static void Saved(IChangeTracking row)
    {
        row.IsNew = false;
        row.ResetModifiedProperties();
    }

    private void EnsureSaves()
    {
        if (_properties is null || _bindColumn is null)
            throw new InvalidOperationException($"The writer of \"{_table}\" has no column binder: the generator that registered it predates SaveAsync; rebuild the assembly that declares the entity.");
        if (_columns.Length > 64)
            throw new NotSupportedException($"\"{_table}\" has {_columns.Length} columns: a save tracks up to 64 (one bit each); use UpdateAsync, which writes every column.");
    }

    /// <summary>The columns the modified properties name, a bit per column; the keys and the version never (the version advances by itself).</summary>
    private ulong MaskOf(IReadOnlySet<string> modified)
    {
        if (modified.Count == 0)
            return 0;
        ulong mask = 0;
        for (var c = 0; c < _columns.Length; c++)
        {
            if (IsKey(c) || c == _versionIndex || IsInsertOnly(c))
                continue;
            if (modified.Contains(_properties![c]) || (_owners![c] is { } owner && modified.Contains(owner)))
                mask |= 1UL << c;
        }

        return mask == 0 ? 0 : mask | UpdatedMask; // a save that writes stamps who and when
    }

    private int ParametersPerRow(ulong mask) => BitOperations.PopCount(mask) + _keyIndexes.Length + (_versionIndex >= 0 ? 1 : 0);

    /// <summary>
    ///     How many rows of a run go in one command: up to 256 within the parameter budget where a round trip costs (PostgreSQL, SQL
    ///     Server); one on SQLite, in process, where the prepared single-row statement executed per row is the cheapest path (its
    ///     provider binds every parameter of a command to every statement of it: a command of N statements costs N² lookups).
    /// </summary>
    private int RunSize(SqlDialect dialect, ulong mask, int budget) => dialect == SqlDialect.Sqlite ? 1 : Math.Min(LargestBatch, budget / ParametersPerRow(mask));

    private WritePlan SavePlan(SqlDialect dialect, ulong mask, int rows, out bool cached)
    {
        var key = (dialect, mask, rows);
        if (_savePlans.TryGetValue(key, out var plan))
        {
            cached = true;
            return plan;
        }

        cached = _savePlans.Count < CachedSavePlans;
        var built = new WritePlan(cached ? SqlPlanIds.Next() : -1, Save(dialect, mask, rows), SaveKinds(mask, rows));
        return cached ? _savePlans.GetOrAdd(key, built) : built;
    }

    private void BindSave(T row, ulong mask, DbParameterCollection parameters, int offset, SqlDialect dialect)
    {
        var i = offset;
        for (var c = 0; c < _columns.Length; c++)
        {
            if ((mask & (1UL << c)) != 0)
                _bindColumn!(row, c, parameters, i++, dialect);
        }

        _bindKeys(row, parameters, i, dialect);
        i += _keyIndexes.Length;
        if (_versionIndex >= 0)
            _bindColumn!(row, _versionIndex, parameters, i, dialect);
    }

    private SqlParamKind[] SaveKinds(ulong mask, int rows)
    {
        var kinds = new SqlParamKind[ParametersPerRow(mask) * rows];
        var i = 0;
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < _columns.Length; c++)
            {
                if ((mask & (1UL << c)) != 0)
                    kinds[i++] = _kinds[c];
            }

            foreach (var k in _keyIndexes)
                kinds[i++] = _kinds[k];
            if (_versionIndex >= 0)
                kinds[i++] = _kinds[_versionIndex];
        }

        return kinds;
    }

    /// <summary>UPDATE of the masked columns (and the version, advanced) by key (and version), <paramref name="rows" /> statements in a row, the parameters numbered through.</summary>
    private string Save(SqlDialect dialect, ulong mask, int rows)
    {
        var sql = new StringBuilder();
        var per = ParametersPerRow(mask);
        for (var r = 0; r < rows; r++)
        {
            var p = r * per;
            if (r > 0)
                sql.Append("; ");
            sql.Append("UPDATE ").Append(SqlDialects.Quote(dialect, _table)).Append(" SET ");
            var first = true;
            for (var c = 0; c < _columns.Length; c++)
            {
                if ((mask & (1UL << c)) == 0)
                    continue;
                sql.Append(first ? "" : ", ").Append(SqlDialects.Quote(dialect, _columns[c])).Append(" = ").Append(Parameter(dialect, c, p++));
                first = false;
            }

            var native = Native(dialect); // the database advances its own version and the statement reads it back
            if (_versionIndex >= 0 && !native)
            {
                var version = SqlDialects.Quote(dialect, _columns[_versionIndex]);
                sql.Append(first ? "" : ", ").Append(version).Append(" = ").Append(version).Append(" + 1");
            }

            if (native && dialect == SqlDialect.SqlServer)
                sql.Append(VersionOutput(dialect));
            sql.Append(" WHERE ");
            for (var k = 0; k < _keyIndexes.Length; k++)
                sql.Append(k > 0 ? " AND " : "").Append(SqlDialects.Quote(dialect, _columns[_keyIndexes[k]])).Append(" = @p").Append((p++).ToString(CultureInfo.InvariantCulture));
            if (_versionIndex >= 0)
                sql.Append(" AND ").Append(VersionCondition(dialect)).Append(" = @p").Append(p.ToString(CultureInfo.InvariantCulture));
            if (native && dialect != SqlDialect.SqlServer)
                sql.Append(VersionOutput(dialect));
        }

        return sql.ToString();
    }
}
