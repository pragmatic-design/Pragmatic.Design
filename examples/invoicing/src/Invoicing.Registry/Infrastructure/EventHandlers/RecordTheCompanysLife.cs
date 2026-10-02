using Invoicing.Registry.Events;
using Invoicing.Registry.Infrastructure.Audit;
using Pragmatic.Audit;

namespace Invoicing.Registry.Infrastructure.EventHandlers;

/// <summary>
///     Records a company entering the service and leaving it, in the audit trail.
/// </summary>
/// <remarks>
///     <para>
///         These are the two facts nobody can read off the row afterwards. <c>State = Suspended</c> does
///         not say <em>when</em>, or that it is a change rather than how the company always was — and the
///         creation timestamp of a row is not the same statement as "this company was taken into the
///         service".
///     </para>
///     <para>
///         ⚠️ The entries carry the company's <b>id</b> as their target and its slug as the detail, and
///         no <c>TenantId</c>: a company is what a tenant id names, so an entry about it does not belong
///         to one. That is the same reason <c>Organization</c> is the one entity in this application
///         that is not <c>ITenantEntity</c>.
///     </para>
/// </remarks>
[EventHandler]
internal sealed class RecordTheCompanysLife(IAuditTrail trail)
    : IDomainEventHandler<OrganizationOnboarded>, IDomainEventHandler<OrganizationSuspended>
{
    public Task HandleAsync(OrganizationOnboarded @event, CancellationToken ct = default)
        => RecordAsync(
            RegistryAuditOperations.OrganizationOnboarded, @event.OrganizationId, @event.OccurredAt,
            $"{@event.Slug}: {@event.LegalName}", ct);

    public Task HandleAsync(OrganizationSuspended @event, CancellationToken ct = default)
        => RecordAsync(
            RegistryAuditOperations.OrganizationSuspended, @event.OrganizationId, @event.OccurredAt,
            @event.Slug, ct);

    private async Task RecordAsync(
        string operation, Guid organization, DateTimeOffset at, string detail, CancellationToken ct) =>
        await trail.RecordAsync(new AuditEntry
        {
            SegmentId = string.Empty,
            OccurredAt = at,
            Category = AuditCategory.Data,
            Operation = operation,
            TargetType = RegistryAuditOperations.OrganizationTarget,
            TargetId = organization.ToString(),
            Detail = detail,
            Outcome = AuditOutcome.Success
        }, ct).ConfigureAwait(false);
}
