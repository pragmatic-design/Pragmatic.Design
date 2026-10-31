using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

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
    ///     Stages <paramref name="entry" /> shaped by the trail's registered naming and redactor, read from the application's services
    ///     <paramref name="db" /> was resolved from: what a writer inside somebody else's save (the <c>[Audited]</c> interceptor, a bulk
    ///     write's aggregate entry) uses, so its entries land in the segments the sealer names and closes.
    /// </summary>
    public static void Stage(DbContext db, AuditEntry entry, TimeProvider timeProvider)
        => Stage(db, entry, PreparerFor(db, timeProvider));

    /// <summary>
    ///     The preparer of the trail <paramref name="db" />'s application registered: its <see cref="IAuditSegmentNaming" /> and
    ///     <see cref="IAuditDetailRedactor" />, as <c>AddAuditTrail</c> registers them (replaceable with <c>TryAdd</c>). The defaults when the
    ///     context has no application services or the application registered no trail, which then has no sealer to disagree with.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Built with its own hourly naming, an <c>[Audited]</c> writer filed entries under ids a registered naming did not produce, and the
    ///     sealer, which asks that naming when each segment's window ends, threw on the first of them: the whole pass sealed nothing.
    ///     <c>CoreOptionsExtension.ApplicationServiceProvider</c> is the request's scoped provider with <c>AddDbContext</c>; EF Core's
    ///     <c>GetInfrastructure()</c> is its internal one, which knows none of the application's services.
    /// </remarks>
    public static AuditEntryPreparer PreparerFor(DbContext db, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var services = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
        return new AuditEntryPreparer(
            services?.GetService<IAuditDetailRedactor>() ?? new PatternAuditDetailRedactor(),
            services?.GetService<IAuditSegmentNaming>() ?? new HourlyAuditSegmentNaming(),
            timeProvider);
    }

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
