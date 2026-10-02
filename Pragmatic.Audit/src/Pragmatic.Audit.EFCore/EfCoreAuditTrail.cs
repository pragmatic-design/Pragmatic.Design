using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Pragmatic.Audit.EFCore;

/// <summary>
///     Appends entries to the trail.
/// </summary>
/// <remarks>
///     Appends take no lock and coordinate with nobody: the segment is derived from the timestamp and
///     <c>Seq</c> comes from the database identity. That is the whole reason the chain links segments
///     rather than entries — a per-entry chain would need a total order, and the trail would become the
///     bottleneck of everything that records anything.
/// </remarks>
internal sealed class EfCoreAuditTrail(
    AuditDbContext db,
    IAuditDetailRedactor redactor,
    IAuditSegmentNaming naming,
    TimeProvider timeProvider) : IAuditTrail, ITransactionalAuditTrail
{
    private readonly AuditEntryPreparer _preparer = new(redactor, naming, timeProvider);

    /// <summary>
    ///     Appends inside the caller's transaction, so the change and its audit row share a fate.
    /// </summary>
    /// <remarks>
    ///     The connection check is the whole safety of this method. EF will happily save on its own
    ///     connection if asked to, and the result would be an audit row that commits independently of
    ///     the change it describes — a trail that disagrees with the data while looking correct, which
    ///     is worse than not offering enlistment at all. So a mismatch throws and says what to fix.
    /// </remarks>
    public async ValueTask RecordAsync(
        AuditEntry entry, DbTransaction transaction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var mine = db.Database.GetDbConnection();
        if (!ReferenceEquals(mine, transaction.Connection))
            throw new InvalidOperationException(
                "The audit trail cannot join this transaction: its DbContext is on a different " +
                "connection, so anything written here would commit independently of the change it " +
                "records. Register AuditDbContext on the same connection as the caller (its tables must " +
                "live in the same database), or use IAuditTrail and accept a non-atomic write.");

        var already = db.Database.CurrentTransaction;
        var enlistedHere = already?.GetDbTransaction() != transaction;

        if (enlistedHere)
            await db.Database.UseTransactionAsync(transaction, ct).ConfigureAwait(false);

        try
        {
            await AppendAsync(entry, ct).ConfigureAwait(false);
        }
        finally
        {
            // Released even on failure. The context outlives the caller's transaction — it is scoped to
            // the request, not to the unit of work — and a context still bound to a transaction that
            // has since been committed or rolled back throws on its next query, somewhere unrelated.
            // The save has already run on the transaction by this point, so letting go changes nothing
            // about what commits.
            if (enlistedHere)
                await db.Database.UseTransactionAsync(null, ct).ConfigureAwait(false);
        }
    }

    public ValueTask RecordAsync(AuditEntry entry, CancellationToken ct = default)
        => AppendAsync(entry, ct);

    private async ValueTask AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        // Shaping lives in AuditEntryPreparer so every writer agrees on it — see its remarks.
        _preparer.Prepare(entry);

        var segment = await db.Segments
            .FirstOrDefaultAsync(s => s.SegmentId == entry.SegmentId, ct)
            .ConfigureAwait(false);

        if (segment is null)
        {
            db.Segments.Add(new AuditSegment
            {
                SegmentId = entry.SegmentId,
                OpenedAt = entry.OccurredAt,
                EntryCount = 0
            });
        }
        else if (segment.IsSealed)
        {
            // Refused rather than absorbed. An entry arriving after its segment was sealed would sit
            // outside the Merkle root, and the next verification would report it as tampering — so the
            // failure surfaces here, where it can be understood, instead of as a false alarm later.
            throw new InvalidOperationException(
                $"Segment '{entry.SegmentId}' is already sealed; an entry timestamped {entry.OccurredAt:O} " +
                "cannot be appended to it. This means a write outlived the sealing grace period.");
        }

        db.Entries.Add(entry);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
