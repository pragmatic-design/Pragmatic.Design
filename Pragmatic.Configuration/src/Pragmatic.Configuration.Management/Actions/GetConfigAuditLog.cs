using Pragmatic.Actions.Abstractions;
using Pragmatic.Audit;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Configuration.Management.Actions;

/// <summary>
///     Query: retrieves configuration changes from the framework audit trail.
///     Requires an <see cref="IAuditTrailReader"/> to be registered (e.g. via AddAuditTrail).
/// </summary>
[DomainAction]
[RequirePermission("configuration.audit.read")]
[BelongsTo<ConfigurationManagementPackage>]
public sealed partial class GetConfigAuditLog : DomainAction<List<ConfigAuditEntry>>
{
    private IAuditTrailReader? _trail;
    private ICurrentUser _currentUser = null!;

    /// <summary>Filter by key prefix. Empty = all.</summary>
    public string KeyPrefix { get; set; } = "";

    /// <summary>Optional tenant scope. When set, only entries for that tenant are returned.</summary>
    public string? TenantId { get; set; }

    /// <summary>Max entries to return. Default: 50.</summary>
    public int Limit { get; set; } = 50;

    public override async Task<Result<List<ConfigAuditEntry>, IError>> Execute(CancellationToken ct = default)
    {
        if (_trail is null)
            return BadRequestError.Create("AuditTrailNotConfigured");

        // No TenantId asks the trail for every tenant's changes, not for the base values' alone.
        if (!TenantBinding.Permits(_currentUser, TenantId))
            return ForbiddenError.ActionDenied("get-config-audit-log", $"tenant:{TenantId ?? "all"}");

        var page = await _trail.QueryAsync(
            new AuditQuery
            {
                Category = AuditCategory.Configuration,
                TenantId = TenantId,
                Limit = Limit,
            },
            ct).ConfigureAwait(false);

        // The prefix filters here rather than in the query: the trail has no notion of a configuration
        // key hierarchy, and teaching it one would put this module's vocabulary into a shared contract.
        return page.Entries
            .Where(e => KeyPrefix.Length == 0
                || (e.TargetId?.StartsWith(KeyPrefix, StringComparison.Ordinal) ?? false))
            .Select(e => new ConfigAuditEntry(
                e.TargetType ?? "", e.TargetId ?? "", e.TenantId,
                e.Operation, e.ValueHash, e.ActorRef, e.OccurredAt))
            .ToList();
    }
}
