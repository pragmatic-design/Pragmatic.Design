using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The columns a writer fills itself (the repository's <c>[Auditable]</c> and <c>[SoftDelete]</c>, read by name): who and when, from the clock and
///     <see cref="SqlContext.Current" />'s user. <see cref="Created" /> stamps an insert (CreatedAt/By, then never written again: an update keeps the
///     stored value), <see cref="Updated" /> every write (UpdatedAt/By), <see cref="Deleted" /> a soft delete or a restore (IsDeleted, DeletedAt/By).
///     The setters are generated: no reflection.
/// </summary>
public sealed class SqlStamps<T>(Action<T, DateTimeOffset, string?>? created, Action<T, DateTimeOffset, string?>? updated, Action<T, bool, DateTimeOffset, string?>? deleted, string[] insertOnlyColumns, string[] updatedColumns, string[] deletedColumns)
{
    public Action<T, DateTimeOffset, string?>? Created { get; } = created;

    public Action<T, DateTimeOffset, string?>? Updated { get; } = updated;

    /// <summary>Marks the row deleted (true) or restored (false): IsDeleted, and DeletedAt/By set or cleared.</summary>
    public Action<T, bool, DateTimeOffset, string?>? Deleted { get; } = deleted;

    /// <summary>The columns written by an insert only (CreatedAt/By): an update or an upsert's update keeps what the row holds.</summary>
    public string[] InsertOnlyColumns { get; } = insertOnlyColumns;

    public string[] UpdatedColumns { get; } = updatedColumns;

    /// <summary>The soft delete's columns: IsDeleted first, then DeletedAt and DeletedBy when the entity has them.</summary>
    public string[] DeletedColumns { get; } = deletedColumns;
}

public sealed partial class SqlWriter<T>
{
    /// <summary>The columns this writer stamps; null when the entity is neither [Auditable] nor [SoftDelete].</summary>
    public SqlStamps<T>? Stamps { get; init; }

    private ulong? _updatedMask;
    private ulong? _deletedMask;

    private ulong UpdatedMask => _updatedMask ??= MaskOfColumns(Stamps?.UpdatedColumns);

    private ulong DeletedMask => _deletedMask ??= MaskOfColumns(Stamps?.DeletedColumns);

    /// <summary>True for a [SoftDelete] entity: a delete is an UPDATE of its deleted columns, the row stays.</summary>
    public bool SoftDeletes => Stamps?.Deleted is not null;

    bool ISqlWriter.SoftDeletes => SoftDeletes;

    private ulong MaskOfColumns(string[]? names)
    {
        ulong mask = 0;
        if (names is null)
            return mask;
        for (var c = 0; c < _columns.Length && c < 64; c++)
        {
            if (Array.IndexOf(names, _columns[c]) >= 0)
                mask |= 1UL << c;
        }

        return mask;
    }

    /// <summary>True for a column an update leaves as stored (CreatedAt/By): written by the insert only.</summary>
    private bool IsInsertOnly(int column) => Stamps is { } stamps && Array.IndexOf(stamps.InsertOnlyColumns, _columns[column]) >= 0;

    private void StampInsert(T row)
    {
        if (Stamps is not { } stamps)
            return;
        var now = DateTimeOffset.UtcNow;
        var user = SqlContext.Current.UserId;
        stamps.Created?.Invoke(row, now, user);
        stamps.Updated?.Invoke(row, now, user);
    }

    private void StampInsert(IReadOnlyList<T> rows)
    {
        if (Stamps is null)
            return;
        foreach (var row in rows)
            StampInsert(row);
    }

    private void StampUpdate(T row) => Stamps?.Updated?.Invoke(row, DateTimeOffset.UtcNow, SqlContext.Current.UserId);

    /// <summary>Restores a soft-deleted row: IsDeleted false, DeletedAt/By cleared, at its version; the roll-ups it feeds take it back.</summary>
    public async Task RestoreAsync(SqlSession session, T row, CancellationToken ct)
    {
        RefuseNative(session.Dialect);
        if (!SoftDeletes)
            throw new NotSupportedException($"\"{_table}\" is not [SoftDelete]: there is no deleted row to restore.");
        if (HasRollUps(session))
        {
            using var own = session.BeginTransaction();
            await RestoreAsync(session, row, ct).ConfigureAwait(false);
            own.Commit();
            return;
        }

        if (RollUps.Length > 0)
            await RollUpAsync(session, add: false, row, ct).ConfigureAwait(false);
        await MarkDeletedAsync(session, row, deleted: false, ct).ConfigureAwait(false);
        if (RollUps.Length > 0)
            await RollUpAsync(session, add: true, row, ct).ConfigureAwait(false);
    }

    /// <summary>The soft delete (or restore) of one row: its deleted and updated columns written at its version, as a save of those columns.</summary>
    private async Task MarkDeletedAsync(SqlSession session, T row, bool deleted, CancellationToken ct)
    {
        EnsureSaves();
        Stamps!.Deleted!(row, deleted, DateTimeOffset.UtcNow, SqlContext.Current.UserId);
        StampUpdate(row);
        var mask = DeletedMask | UpdatedMask;
        var plan = SavePlan(session.Dialect, mask, 1, out var cached);
        var command = cached ? session.PreparedCommand(plan.Id, plan.Sql, plan.Kinds) : session.FreshCommand(plan.Sql, plan.Kinds);
        try
        {
            BindSave(row, mask, command.Parameters, 0, session.Dialect);
            var affected = await session.RunAsync(command, Origin(deleted ? "soft delete" : "restore"), ct).ConfigureAwait(false);
            if (affected == 0 && _versionIndex >= 0)
                throw new SqlConcurrencyException(_table, _key(row), _version!(row));
        }
        finally
        {
            if (!cached)
                command.Dispose();
        }

        _bumpVersion?.Invoke(row);
    }

    /// <summary>The soft delete of one row as an item of a write batch, marked at <paramref name="at" /> (now when null).</summary>
    private SqlWriteItem SoftDeleteItem(T row, SqlDialect dialect, DateTimeOffset? at = null)
    {
        RefuseNative(dialect);
        EnsureSaves();
        Stamps!.Deleted!(row, true, at ?? DateTimeOffset.UtcNow, SqlContext.Current.UserId);
        StampUpdate(row);
        var mask = DeletedMask | UpdatedMask;
        var plan = SavePlan(dialect, mask, 1, out _);
        return new SqlWriteItem(_table, plan.Id, plan.Kinds, d => plan.Sql, (p, o, d) => BindSave(row, mask, p, o, d), expected: 1)
        {
            Completed = _bumpVersion is null ? null : () => _bumpVersion(row),
            Conflict = _versionIndex >= 0 ? _ => new SqlConcurrencyException(_table, _key(row), _version!(row)) : null,
        };
    }

    /// <summary>
    ///     The soft delete of the live rows a condition selects (a cascade's or a graph's orphans): <c>UPDATE … SET IsDeleted = true, DeletedAt = now,
    ///     DeletedBy = user WHERE (…) AND IsDeleted = false</c>, the condition's parameters first.
    /// </summary>
    private SqlWriteItem SoftDeleteWhere(Func<SqlDialect, string> where, SqlParamKind[] kinds, Action<DbParameterCollection, int, SqlDialect> bind, DateTimeOffset? at = null)
    {
        var columns = Stamps!.DeletedColumns;
        var now = at ?? DateTimeOffset.UtcNow;
        var user = SqlContext.Current.UserId;
        var stamped = new List<(string Column, SqlParamKind Kind, object? Value)>();
        for (var i = 1; i < columns.Length; i++)
        {
            var kind = _kinds[Array.IndexOf(_columns, columns[i])];
            stamped.Add((columns[i], kind, kind == SqlParamKind.String ? user : kind == SqlParamKind.DateTime ? now.UtcDateTime : now));
        }

        var allKinds = new SqlParamKind[kinds.Length + stamped.Count];
        kinds.CopyTo(allKinds, 0);
        for (var i = 0; i < stamped.Count; i++)
            allKinds[kinds.Length + i] = stamped[i].Kind;
        return new SqlWriteItem(_table, -1, allKinds, dialect =>
        {
            var sql = new StringBuilder("UPDATE ").Append(SqlDialects.Quote(dialect, _table)).Append(" SET ").Append(SqlDialects.Quote(dialect, columns[0])).Append(" = ").Append(SqlDialects.ResolveBooleanLiterals("{bt:1}", dialect));
            for (var i = 0; i < stamped.Count; i++)
                sql.Append(", ").Append(SqlDialects.Quote(dialect, stamped[i].Column)).Append(" = @p").Append((kinds.Length + i).ToString(CultureInfo.InvariantCulture));
            // The live rows only: one deleted before keeps who deleted it and when.
            return sql.Append(" WHERE (").Append(where(dialect)).Append(") AND ").Append(SqlDialects.Quote(dialect, columns[0])).Append(" = ").Append(SqlDialects.ResolveBooleanLiterals("{bt:0}", dialect)).ToString();
        }, (p, o, d) =>
        {
            bind(p, o, d);
            for (var i = 0; i < stamped.Count; i++)
                p[o + kinds.Length + i].Value = SqlValue.ToDatabase(d, stamped[i].Value);
        }, expected: -1);
    }

    SqlWriteItem ISqlWriter.SoftDeleteWhereItem(Func<SqlDialect, string> where, SqlParamKind[] parentKeyKinds, object?[] parentKey, DateTimeOffset at)
        => SoftDeleteWhere(where, parentKeyKinds, BindParentKey(parentKey), at);

    SqlWriteItem ISqlWriter.SoftDeleteItemAt(object row, SqlDialect dialect, DateTimeOffset at) => SoftDeleteItem((T)row, dialect, at);
}
