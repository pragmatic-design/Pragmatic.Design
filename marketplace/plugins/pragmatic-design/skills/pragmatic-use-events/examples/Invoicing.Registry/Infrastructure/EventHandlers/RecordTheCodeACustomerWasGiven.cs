using Invoicing.Registry.Events;
using Invoicing.Registry.Infrastructure.Audit;
using Pragmatic.Audit;

namespace Invoicing.Registry.Infrastructure.EventHandlers;

/// <summary>
///     Records which code a customer was given, in the audit trail.
/// </summary>
/// <remarks>
///     <para>
///         <c>[Audited]</c> on the customer already records that the row was written. This records the
///         code it was written with — the one thing about a customer that is quoted outside the
///         application, on an invoice and on the telephone, and the one thing nobody chose: it comes
///         from a sequence, so which code a customer got is a fact of the moment they were added.
///     </para>
///     <para>
///         Taken from the event, not read back off the row. That is the point of the event carrying it,
///         and a handler that read the row would answer a different question — what the code is now —
///         which for a trail is not the same statement.
///     </para>
/// </remarks>
[EventHandler]
internal sealed class RecordTheCodeACustomerWasGiven(IAuditTrail trail)
    : IDomainEventHandler<CustomerRegistered>
{
    public async Task HandleAsync(CustomerRegistered @event, CancellationToken ct = default) =>
        await trail.RecordAsync(new AuditEntry
        {
            SegmentId = string.Empty,
            OccurredAt = @event.OccurredAt,
            Category = AuditCategory.Data,
            Operation = RegistryAuditOperations.CustomerRegistered,
            TargetType = RegistryAuditOperations.CustomerTarget,
            TargetId = @event.CustomerId.ToString(),
            Detail = $"{@event.Code}: {@event.Name}",
            Outcome = AuditOutcome.Success
        }, ct).ConfigureAwait(false);
}
