namespace Pragmatic.Persistence.Documents;

/// <summary>
///     What an application hangs on a session's writes (<see cref="DocSession.CommitHooks" />): a callback before the documents are written and
///     one once they are committed. A write outside a transaction (every write on Cosmos DB, and on a MongoDB that runs none) is its own commit; a
///     write inside one is reported when the transaction commits, and never when it is aborted. Pragmatic's <c>ICommitObserver</c> is attached
///     through these (<c>DocCommitObservers</c>). Null costs nothing.
/// </summary>
public sealed class DocCommitHooks
{
    /// <summary>Before the command that writes the documents is sent. Throwing refuses the write: nothing is written.</summary>
    public Action<IReadOnlyList<DocWrittenDocument>>? Writing { get; init; }

    /// <summary>After the commit that carried the documents succeeded, before its caller sees it.</summary>
    public Action<IReadOnlyList<DocWrittenDocument>>? Committed { get; init; }
}
