using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Audit.Management.Actions;

/// <summary>
///     Command: discards sealed audit segments older than a retention window, and declares the gap.
/// </summary>
/// <remarks>
///     <para>
///         Whole segments, never individual entries: removing an entry from a sealed segment changes its
///         Merkle root, so retention would be indistinguishable from tampering — which destroys the one
///         property the trail exists for. The gap is recorded as a <c>PrunedRange</c> carrying the hash
///         the following segment still links to, and verification steps over a declared gap.
///     </para>
///     <para>
///         The window is an input rather than configuration because the answer is a legal one and it
///         differs per deployment. Call it from a scheduled job, an admin screen, or by hand — the
///         framework does not decide when.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission("audit.trail.prune")]
[BelongsTo<AuditManagementPackage>]
public sealed partial class PruneAuditTrail : DomainAction<PruneAuditTrailResult>
{
    private IAuditRetention _retention = null!;

    /// <summary>How much history to keep, in days. Everything sealed before that is discarded.</summary>
    public required int RetentionDays { get; set; }

    public override async Task<Result<PruneAuditTrailResult, IError>> Execute(CancellationToken ct = default)
    {
        ThrowIfNull(_retention, nameof(_retention));

        // Zero would discard everything sealed, which is a different operation and never what a
        // retention window means. Refused rather than honoured.
        if (RetentionDays <= 0)
            return BadRequestError.Create("RetentionDaysMustBePositive");

        var range = await _retention
            .PruneOlderThanAsync(TimeSpan.FromDays(RetentionDays), ct)
            .ConfigureAwait(false);

        return range is null
            ? new PruneAuditTrailResult(false, null, null)
            : new PruneAuditTrailResult(true, range.FromSegmentId, range.UntilSegmentId);
    }
}

/// <summary>
///     What the retention pass discarded.
/// </summary>
/// <remarks>
///     The segment ids rather than a row count: a count says nothing a reader can check, while the two
///     ids name exactly the declared gap that verification will step over.
/// </remarks>
/// <param name="Pruned">Whether anything was old enough to discard.</param>
/// <param name="FromSegmentId">The first discarded segment, or null when nothing was.</param>
/// <param name="ToSegmentId">The last discarded segment, or null when nothing was.</param>
public readonly record struct PruneAuditTrailResult(bool Pruned, string? FromSegmentId, string? ToSegmentId);
