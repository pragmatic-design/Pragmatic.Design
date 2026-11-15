namespace Pragmatic.Persistence.Documents;

/// <summary>
///     A module's phase of a save (the audit log, the outboxes), after the unit of work's own: it reads what the save writes and writes its own
///     documents through the session, in the save's transaction where there is one (MongoDB on a replica set), as <c>ISqlSavePhase</c> adds its
///     rows to the Sql engine's batch.
/// </summary>
public interface IDocSavePhase
{
    ValueTask ApplyAsync(DocSaveContext save, CancellationToken ct);

    /// <summary>
    ///     Whether the phase has documents to write even when the save writes no entity. A save with nothing to write returns before its phases,
    ///     unless one of them answers true: then only the phases run, in one transaction where the server has them (#803). An outbox answers
    ///     true while an operation's events are staged; a phase that only follows the entities keeps the default.
    /// </summary>
    bool HasPendingWork => false;
}
