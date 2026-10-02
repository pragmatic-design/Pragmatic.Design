using Pragmatic.Audit;

namespace TimeOff.Leave.Compliance.Actions;

/// <summary>
///     Re-hashes the sealed segments of the audit trail in a period and follows their chain: an entry
///     altered, removed or added since it was sealed shows up here, naming its segment.
/// </summary>
/// <remarks>
///     Without a period, the whole trail. The trail holds references and never a name or an email, so it
///     verifies after an erasure exactly as before it.
/// </remarks>
[DomainAction]
[RequirePermission(LeavePermissions.AuditTrail.Verify)]
[Endpoint(HttpVerb.Get, "api/compliance/audit-trail/integrity")]
public partial class VerifyAuditTrailAction : DomainAction<AuditIntegrityDto>
{
    private IAuditTrailReader _trail = null!;

    /// <summary>From this instant; the beginning of the trail when absent.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Until this instant; the end of the trail when absent.</summary>
    public DateTimeOffset? Until { get; init; }

    public override async Task<Result<AuditIntegrityDto, IError>> Execute(CancellationToken ct = default)
    {
        var report = await _trail
            .VerifyAsync(From ?? DateTimeOffset.MinValue, Until ?? DateTimeOffset.MaxValue, ct)
            .ConfigureAwait(false);

        return AuditIntegrityDto.From(report);
    }
}
