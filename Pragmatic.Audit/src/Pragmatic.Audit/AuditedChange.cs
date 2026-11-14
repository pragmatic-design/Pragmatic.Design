using System.Diagnostics;
using Pragmatic.Identity;
using Pragmatic.Pipeline;

namespace Pragmatic.Audit;

/// <summary>
///     The entry a write to an <c>[Audited]</c> entity records, whatever store persists the entity: EF Core's audit-log interceptor and the
///     engines' save phases build it here, so the trail reads the same whichever wrote it.
/// </summary>
public static class AuditedChange
{
    // Constants per shape rather than "Data." + action: an operation assembled at runtime cannot be found by searching the source.

    /// <summary>The entity was added.</summary>
    public const string EntityCreated = "Data.EntityCreated";

    /// <summary>The entity was modified.</summary>
    public const string EntityUpdated = "Data.EntityUpdated";

    /// <summary>The entity was deleted, or soft-deleted: the trail records the domain's meaning, not the store's.</summary>
    public const string EntityDeleted = "Data.EntityDeleted";

    /// <summary>A soft-deleted entity was restored.</summary>
    public const string EntityRestored = "Data.EntityRestored";

    /// <summary>
    ///     The entry for one change: by <paramref name="currentUser" /> (or nobody), at <paramref name="now" />, inside the ambient business
    ///     operation and trace. Its segment is assigned when <see cref="AuditEntryPreparer" /> prepares it.
    /// </summary>
    public static AuditEntry Entry(string operation, string? targetType, string? targetId, DateTimeOffset now, ICurrentUser? currentUser)
    {
        // ICurrentUser.Id stays the SUBJECT throughout a delegated call — that is what keeps ownership
        // stamps, scope filters and preferences behaving. So reading it alone recorded the person an
        // agent was working for in the field named "who acted", and the agent nowhere at all. In an
        // application with first-class delegation that is the one question a trail is asked.
        var delegation = currentUser?.Delegation;
        var subjectId = currentUser?.IdOrNull();

        return new AuditEntry
        {
            SegmentId = string.Empty, // assigned by the preparer
            OccurredAt = now,
            Category = AuditCategory.Data,
            Operation = operation,
            // Already a reference by the time a message or a request reaches persistence: the
            // identity is pseudonymised where it enters the system, not looked up here.
            ActorRef = delegation?.ActorId ?? subjectId,
            OnBehalfOfRef = delegation is null ? null : subjectId,
            // The tenant the caller acted in (#750): it was never set, so an entry could not be scoped to a tenant at all.
            TenantId = currentUser?.TenantId,
            // The trace id when something traces, which correlates across services; the operation's own correlation otherwise (#750),
            // because the trace id is null wherever no listener is registered and the entries of one operation were then tied to nothing.
            CorrelationId = Activity.Current?.TraceId.ToString() ?? OperationScope.Correlation,
            // What the application was doing, as opposed to what happened to the row. Read from
            // the ambient the invokers set rather than from Activity.Current, which is null
            // wherever no tracing listener is registered.
            BusinessOperation = OperationScope.Current,
            TargetType = targetType,
            TargetId = targetId,
            Outcome = AuditOutcome.Success,
        };
    }
}
