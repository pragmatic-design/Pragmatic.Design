namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The rows this session writes, reported to <see cref="CommitHooks" />: before the statements run, and once their commit succeeded. A write
///     outside a transaction is its own commit; inside one the rows wait for its commit, and a rollback forgets them.
/// </summary>
public sealed partial class SqlSession
{
    private List<SqlWrittenRow>? _uncommitted;

    // How many rows the transaction had written when each savepoint was set: a rollback to it forgets the ones after, so CommitHooks never
    // sees as committed a row the transaction undid.
    private Dictionary<string, int>? _savepointMarks;

    /// <summary>
    ///     What sees the rows this session writes; null costs nothing. Settable rather than init-only: a pooled session is opened before the
    ///     request that rents it is known, and the request's scope attaches its own observers.
    /// </summary>
    public SqlCommitHooks? CommitHooks { get; set; }

    /// <summary>Before the statements that write <paramref name="rows" /> run; throwing refuses them.</summary>
    internal void Writing(IReadOnlyList<SqlWrittenRow> rows)
    {
        if (rows.Count > 0)
            CommitHooks?.Writing?.Invoke(rows);
    }

    /// <summary>After the statements that wrote <paramref name="rows" /> ran: committed now outside a transaction, at its commit inside one.</summary>
    internal void Wrote(IReadOnlyList<SqlWrittenRow> rows)
    {
        if (rows.Count == 0 || CommitHooks is null)
            return;
        if (Transaction is null)
            CommitHooks.Committed?.Invoke(rows);
        else
            (_uncommitted ??= []).AddRange(rows);
    }

    /// <summary>A savepoint set in the transaction (by the session or through its transaction's <c>Save</c>): the rows written so far, marked.</summary>
    private void MarkSavepoint(string name)
    {
        if (CommitHooks is not null)
            (_savepointMarks ??= new Dictionary<string, int>(StringComparer.Ordinal))[name] = _uncommitted?.Count ?? 0;
    }

    /// <summary>A rollback to a savepoint, which stays set: the rows written after it forgotten.</summary>
    private void RolledBackToSavepoint(string name)
    {
        if (_savepointMarks is not null && _savepointMarks.TryGetValue(name, out var mark) && _uncommitted is { } rows && rows.Count > mark)
            rows.RemoveRange(mark, rows.Count - mark);
    }

    /// <summary>The session's transaction ended: its rows reported when it committed, forgotten when it did not.</summary>
    private void Ended(bool committed)
    {
        var rows = _uncommitted;
        _uncommitted = null;
        _savepointMarks = null;
        if (committed && rows is { Count: > 0 })
            CommitHooks?.Committed?.Invoke(rows);
    }

    /// <summary>A write through the entity's writer, reported around it.</summary>
    private async Task ObservedAsync(SqlWrittenRow[] rows, Func<Task> write)
    {
        Writing(rows);
        await write().ConfigureAwait(false);
        Wrote(rows);
    }

    /// <summary>A write through the entity's writer, reported around it, its result returned.</summary>
    private async Task<TResult> ObservedAsync<TResult>(SqlWrittenRow[] rows, Func<Task<TResult>> write)
    {
        Writing(rows);
        var result = await write().ConfigureAwait(false);
        Wrote(rows);
        return result;
    }

    /// <summary>The rows, each written as <paramref name="kind" />.</summary>
    private static SqlWrittenRow[] Rows<T>(IReadOnlyList<T> rows, SqlWriteKind kind)
    {
        var written = new SqlWrittenRow[rows.Count];
        for (var i = 0; i < rows.Count; i++)
            written[i] = new SqlWrittenRow(rows[i]!, kind);
        return written;
    }

    /// <summary>The rows a save writes: the new ones inserted, the changed ones updated, the untouched ones left out.</summary>
    private static SqlWrittenRow[] Saved<T>(IReadOnlyList<T> rows) where T : class, IChangeTracking
    {
        var written = new List<SqlWrittenRow>(rows.Count);
        foreach (var row in rows)
        {
            if (row.IsNew)
                written.Add(new SqlWrittenRow(row, SqlWriteKind.Insert));
            else if (row.ModifiedProperties.Count > 0)
                written.Add(new SqlWrittenRow(row, SqlWriteKind.Update));
        }

        return [.. written];
    }
}
