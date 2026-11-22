using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The application's <see cref="ICommitObserver" />s on a session: what the generated registration of a boundary's session calls, so every
///     write the request makes through it is seen.
/// </summary>
public static class DocCommitObservers
{
    /// <summary>
    ///     Sets <paramref name="session" />'s <see cref="DocSession.CommitHooks" /> to <paramref name="observers" />, or clears them when there are
    ///     none, and returns the session.
    /// </summary>
    public static DocSession Attach(DocSession session, IEnumerable<ICommitObserver> observers)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(observers);
        var all = observers.ToArray();
        session.CommitHooks = all.Length == 0 ? null : new DocCommitHooks
        {
            Writing = documents =>
            {
                var entries = Entries(documents);
                foreach (var observer in all)
                    observer.Writing(entries);
            },
            Committed = documents =>
            {
                var entries = Entries(documents);
                foreach (var observer in all)
                    observer.Committed(entries);
            },
        };
        return session;
    }

    private static CommitEntry[] Entries(IReadOnlyList<DocWrittenDocument> documents)
    {
        var entries = new CommitEntry[documents.Count];
        for (var i = 0; i < documents.Count; i++)
        {
            entries[i] = new CommitEntry(documents[i].Entity, documents[i].Kind switch
            {
                DocWriteKind.Insert => CommitEntryState.Added,
                DocWriteKind.Delete => CommitEntryState.Deleted,
                _ => CommitEntryState.Modified,
            });
        }

        return entries;
    }
}
