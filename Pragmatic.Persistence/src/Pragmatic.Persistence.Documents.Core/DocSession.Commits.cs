namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The documents this session writes, reported to <see cref="CommitHooks" />: before the command is sent, and once its commit succeeded. A
///     write outside a transaction is its own commit; inside one the documents wait for its commit, and an abort forgets them.
/// </summary>
public sealed partial class DocSession
{
    private List<DocWrittenDocument>? _uncommitted;

    /// <summary>
    ///     What sees the documents this session writes; null costs nothing. Settable rather than init-only: the request's scope attaches its own
    ///     observers to the session it was handed.
    /// </summary>
    public DocCommitHooks? CommitHooks { get; set; }

    /// <summary>A write, reported around it: <paramref name="write" /> runs only when no hook refused it.</summary>
    private async Task ObservedAsync(DocWrittenDocument[] documents, Func<Task> write)
    {
        if (documents.Length > 0)
            CommitHooks?.Writing?.Invoke(documents);
        await write().ConfigureAwait(false);
        Wrote(documents);
    }

    private void Wrote(DocWrittenDocument[] documents)
    {
        if (documents.Length == 0 || CommitHooks is null)
            return;
        if (Transaction is null)
            CommitHooks.Committed?.Invoke(documents);
        else
            (_uncommitted ??= []).AddRange(documents);
    }

    /// <summary>The documents the open transaction wrote, taken as it ends.</summary>
    internal List<DocWrittenDocument>? TakeUncommitted()
    {
        var documents = _uncommitted;
        _uncommitted = null;
        return documents;
    }

    /// <summary>A transaction's commit succeeded: its documents reported.</summary>
    internal void Committed(List<DocWrittenDocument>? documents)
    {
        if (documents is { Count: > 0 })
            CommitHooks?.Committed?.Invoke(documents);
    }

    private static DocWrittenDocument[] One(object entity, DocWriteKind kind) => [new(entity, kind)];
}
