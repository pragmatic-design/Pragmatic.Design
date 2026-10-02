using Pragmatic.Audit;
using TimeOff.Leave.Events;
using TimeOff.Leave.Infrastructure.Audit;

namespace TimeOff.Leave.Infrastructure.EventHandlers;

/// <summary>
///     Records every leave decision in the audit trail: what was decided, on which request, by whom, for
///     whom.
/// </summary>
/// <remarks>
///     <para>
///         The people are there by reference — the employees' ids — and never by name or email: the
///         trail holds no personal value, which is what lets it outlive the erasure of the people it is
///         about and still verify.
///     </para>
///     <para>
///         <c>[Audited]</c> on the request already records that its row changed; this records what the
///         change <em>meant</em>, which a hash of the row cannot say.
///     </para>
/// </remarks>
[EventHandler]
internal sealed class RecordTheDecision(IAuditTrail trail) : IDomainEventHandler<LeaveRequestDecided>
{
    public async Task HandleAsync(LeaveRequestDecided @event, CancellationToken ct = default) =>
        await trail.RecordAsync(new AuditEntry
        {
            SegmentId = string.Empty,
            OccurredAt = @event.OccurredAt,
            Category = AuditCategory.Data,
            Operation = LeaveAuditOperations.For(@event.Status),
            ActorRef = @event.DecidedById?.ToString("N"),
            SubjectRef = @event.EmployeeId.ToString("N"),
            TargetType = LeaveAuditOperations.LeaveRequestTarget,
            TargetId = @event.LeaveRequestId.ToString("N"),
            Outcome = AuditOutcome.Success
        }, ct).ConfigureAwait(false);
}
