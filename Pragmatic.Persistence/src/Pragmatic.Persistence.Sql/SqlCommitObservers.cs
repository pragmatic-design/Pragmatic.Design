using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql.Execution;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The application's <see cref="ICommitObserver" />s on a session: what the generated registration of a boundary's session calls, so every
///     write the request makes through it (the unit of work's, an upload's own transaction, a job store's) is seen.
/// </summary>
public static class SqlCommitObservers
{
    /// <summary>
    ///     Sets <paramref name="session" />'s <see cref="SqlSession.CommitHooks" /> to <paramref name="observers" />, or clears them when there
    ///     are none (a pooled session keeps no other request's), and returns the session.
    /// </summary>
    public static SqlSession Attach(SqlSession session, IEnumerable<ICommitObserver> observers)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(observers);
        var all = observers.ToArray();
        session.CommitHooks = all.Length == 0 ? null : new SqlCommitHooks
        {
            Writing = rows =>
            {
                var entries = Entries(rows);
                foreach (var observer in all)
                    observer.Writing(entries);
            },
            Committed = rows =>
            {
                var entries = Entries(rows);
                foreach (var observer in all)
                    observer.Committed(entries);
            },
        };
        return session;
    }

    private static CommitEntry[] Entries(IReadOnlyList<SqlWrittenRow> rows)
    {
        var entries = new CommitEntry[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            entries[i] = new CommitEntry(rows[i].Row, rows[i].Kind switch
            {
                SqlWriteKind.Insert => CommitEntryState.Added,
                SqlWriteKind.Delete => CommitEntryState.Deleted,
                _ => CommitEntryState.Modified,
            });
        }

        return entries;
    }
}
