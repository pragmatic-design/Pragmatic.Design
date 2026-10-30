using Pragmatic.Identity;
using Pragmatic.Persistence.Documents;

namespace Pragmatic.Audit.Engines.Documents;

/// <summary>
///     The audit trail's phase of a <see cref="DocUnitOfWork" />'s save, registered for a boundary with <c>[Audited]</c> entities: each write to
///     one becomes an <see cref="AuditEntryDocument" /> written through the save's session, and its segment's document when it is the first of
///     the window.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Atomic with the entities only where the save is: in the save's MongoDB transaction when the server runs them (a replica set). On a
///         standalone server and on Cosmos DB the documents are written one by one, these after the entities: a failure in between leaves the
///         entities written and their entries missing.
///     </para>
///     <para>⚠️ An entry whose segment is already sealed is refused, and the save with it, as on the Sql engine.</para>
/// </remarks>
public sealed class DocAuditPhase(AuditEntryPreparer preparer, ICurrentUser? currentUser) : IDocSavePhase
{
    /// <inheritdoc />
    public async ValueTask ApplyAsync(DocSaveContext save, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(save);
        HashSet<string>? segments = null;
        foreach (var written in save.Entries)
        {
            var operation = AuditedEntries.OperationOf(written.Entity, written.State == DocEntryState.Added, written.State == DocEntryState.Deleted,
                written.State == DocEntryState.Modified, written.OriginalIsDeleted);
            if (operation is null)
                continue;

            var entry = AuditedChange.Entry(operation, written.Entity.GetType().Name, AuditedEntries.TargetIdOf(written.Entity), save.Now, currentUser);
            preparer.Prepare(entry);

            // Once per segment and save: several entries of one save share it, and the second must not insert it again.
            if ((segments ??= new HashSet<string>(StringComparer.Ordinal)).Add(entry.SegmentId))
                await DocAuditStore.OpenAsync(save.Session, entry, ct).ConfigureAwait(false);

            await save.Session.InsertAsync(DocAuditStore.Document(entry), ct).ConfigureAwait(false);
        }
    }
}
