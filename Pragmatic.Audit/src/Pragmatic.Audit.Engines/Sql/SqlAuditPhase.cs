using Pragmatic.Identity;
using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Writing;

namespace Pragmatic.Audit.Engines.Sql;

/// <summary>
///     The audit trail's phase of a <see cref="SqlUnitOfWork" />'s save, registered for a boundary with <c>[Audited]</c> entities: each write to
///     one becomes an <see cref="AuditEntryRow" /> in the save's batch, and its segment's row when it is the first of the window, so the entry
///     and the change it records commit or roll back together, as EF Core's interceptor writes them into the context being saved.
/// </summary>
/// <remarks>
///     ⚠️ An entry whose segment is already sealed is refused, and the save with it: it would sit outside the Merkle root, and the next
///     verification would call it tampering. That is a write that outlived the sealing grace period.
/// </remarks>
public sealed class SqlAuditPhase(AuditEntryPreparer preparer, ICurrentUser? currentUser) : ISqlSavePhase
{
    /// <inheritdoc />
    public async ValueTask ApplyAsync(SqlSaveContext save, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(save);
        HashSet<string>? segments = null;
        foreach (var written in save.Entries)
        {
            var operation = AuditedEntries.OperationOf(written.Entity, written.State == SqlEntryState.Added, written.State == SqlEntryState.Deleted,
                written.State == SqlEntryState.Modified, written.OriginalIsDeleted);
            if (operation is null)
                continue;

            var entry = AuditedChange.Entry(operation, written.Entity.GetType().Name, AuditedEntries.TargetIdOf(written.Entity), save.Now, currentUser);
            preparer.Prepare(entry);

            // Once per segment and save: several entries of one save share it, and the second must not insert it again.
            if ((segments ??= new HashSet<string>(StringComparer.Ordinal)).Add(entry.SegmentId))
                await OpenAsync(save.Batch, entry, ct).ConfigureAwait(false);

            save.Batch.InsertAsIs(AuditRecords.From(new AuditEntryRow(), entry));
        }
    }

    /// <summary>The entry's segment, inserted with the save when it is the first of its window; refused when sealed.</summary>
    private static async Task OpenAsync(SqlWriteBatch batch, AuditEntry entry, CancellationToken ct)
    {
        var segment = await batch.Session.Query<AuditSegmentRow>().FirstOrDefaultAsync((s, id) => s.SegmentId == id, entry.SegmentId, ct).ConfigureAwait(false);
        if (segment is null)
            batch.InsertAsIs(new AuditSegmentRow { SegmentId = entry.SegmentId, OpenedAt = entry.OccurredAt.UtcTicks });
        else if (segment.SealedAt is not null)
            throw AuditStore.SealedSegment(entry);
    }
}
