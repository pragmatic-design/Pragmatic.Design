namespace Pragmatic.Persistence.Documents.Pragmatic;

/// <summary>
///     A module's phase of a save (the audit log, the outboxes), after the unit of work's own: it reads what the save writes and writes its own
///     documents through the session, in the save's transaction where there is one (MongoDB on a replica set), as <c>ISqlSavePhase</c> adds its
///     rows to the Sql engine's batch.
/// </summary>
public interface IDocSavePhase
{
    ValueTask ApplyAsync(DocSaveContext save, CancellationToken ct);
}
