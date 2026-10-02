using Invoicing.Billing.Events;
using Invoicing.Billing.Infrastructure.Audit;
using Pragmatic.Audit;

namespace Invoicing.Billing.Infrastructure.EventHandlers;

/// <summary>
///     Records each of an invoice's three moves in the audit trail: issued, settled, voided.
/// </summary>
/// <remarks>
///     <para>
///         <c>[Audited]</c> on the invoice already records that its row changed, and hashes it. This
///         records what the change <em>meant</em> — which a hash cannot say, and which is the question
///         asked of a trail: not "was this row touched" but "when was this invoice settled".
///     </para>
///     <para>
///         The actor is deliberately absent. The move is the fact, and who performed it is already on the
///         <c>[Audited]</c> entry of the same row at the same instant; repeating it here would be a
///         second copy of one truth, and the handler runs after the commit, where the request's identity
///         is no longer something to rely on.
///     </para>
///     <para>
///         One class, three handlers: the three moves differ by a name and nothing else, and three
///         near-identical classes would be three places to change the day the trail gains a field.
///     </para>
/// </remarks>
[EventHandler]
internal sealed class RecordWhatTheMoveMeant(IAuditTrail trail)
    : IDomainEventHandler<InvoiceIssued>,
        IDomainEventHandler<InvoicePaid>,
        IDomainEventHandler<InvoiceVoided>
{
    public Task HandleAsync(InvoiceIssued @event, CancellationToken ct = default)
        => RecordAsync(BillingAuditOperations.InvoiceIssued, @event.InvoiceId, @event.OccurredAt, @event.Number, ct);

    public Task HandleAsync(InvoicePaid @event, CancellationToken ct = default)
        => RecordAsync(BillingAuditOperations.InvoicePaid, @event.InvoiceId, @event.OccurredAt, @event.Number, ct);

    public Task HandleAsync(InvoiceVoided @event, CancellationToken ct = default)
        => RecordAsync(
            BillingAuditOperations.InvoiceVoided, @event.InvoiceId, @event.OccurredAt,
            $"{@event.Number}: {@event.VoidReason}", ct);

    private async Task RecordAsync(
        string operation, Guid invoice, DateTimeOffset at, string? detail, CancellationToken ct) =>
        await trail.RecordAsync(new AuditEntry
        {
            SegmentId = string.Empty,
            OccurredAt = at,
            Category = AuditCategory.Data,
            Operation = operation,
            TargetType = BillingAuditOperations.InvoiceTarget,
            TargetId = invoice.ToString(),
            Detail = detail,
            Outcome = AuditOutcome.Success
        }, ct).ConfigureAwait(false);
}
