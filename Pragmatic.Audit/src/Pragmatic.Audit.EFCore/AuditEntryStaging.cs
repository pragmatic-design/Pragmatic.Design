using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Audit.EFCore;

/// <summary>
///     Queues an entry into a <see cref="DbContext" /> without saving it.
/// </summary>
/// <remarks>
///     <para>
///         For writers that are already inside somebody else's unit of work — an EF interceptor adding
///         audit rows to the context being saved is the case this exists for. Calling
///         <see cref="DbContext.SaveChangesAsync(CancellationToken)" /> there would re-enter a save
///         already in progress; letting the outer save persist both the change and its audit row is
///         both simpler and strictly more atomic than any enlistment.
///     </para>
///     <para>
///         The segment lookup has to happen here rather than being deferred. It is what refuses a write
///         to a segment that has already been sealed — without it such an entry lands outside the
///         Merkle root and the next verification reports tampering, which is the false alarm the whole
///         sealing design exists to avoid.
///     </para>
/// </remarks>
public static class AuditEntryStaging
{
    /// <summary>
    ///     Adds the entry, and its segment if that is the first of the window, to <paramref name="db" />.
    /// </summary>
    /// <remarks>
    ///     The context must map <see cref="AuditEntry" /> and <see cref="AuditSegment" /> — see
    ///     <see cref="AuditDbContext.ApplyAuditConfigurations" />, which exists for consumers keeping
    ///     these tables alongside their own data.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The entry's segment is already sealed.</exception>
    public static void Stage(DbContext db, AuditEntry entry, AuditEntryPreparer preparer)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(preparer);

        preparer.Prepare(entry);

        // Local first: several entries in one save share a segment, and the second must see the row the
        // first one queued rather than trying to add it again.
        var segment = db.Set<AuditSegment>().Local.FirstOrDefault(s => s.SegmentId == entry.SegmentId)
            ?? db.Set<AuditSegment>().FirstOrDefault(s => s.SegmentId == entry.SegmentId);

        if (segment is null)
        {
            db.Add(new AuditSegment
            {
                SegmentId = entry.SegmentId,
                OpenedAt = entry.OccurredAt,
                EntryCount = 0,
            });
        }
        else if (segment.IsSealed)
        {
            throw new InvalidOperationException(
                $"Segment '{entry.SegmentId}' is already sealed; an entry timestamped {entry.OccurredAt:O} " +
                "cannot be appended to it. This means a write outlived the sealing grace period.");
        }

        db.Add(entry);
    }
}
