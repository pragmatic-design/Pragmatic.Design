using Pragmatic.Specification;

namespace Pragmatic.Audit.Engines.Sql;

/// <summary>The reader's filters on the Sql engine, one per field of <see cref="AuditQuery" />, composed at runtime: what is not asked for is not filtered.</summary>
public static partial class AuditEntryRowSpecifications
{
    public static Specification<AuditEntryRow> From(long ticks) => Spec<AuditEntryRow>.Where(e => e.OccurredAt >= ticks);

    public static Specification<AuditEntryRow> Until(long ticks) => Spec<AuditEntryRow>.Where(e => e.OccurredAt < ticks);

    public static Specification<AuditEntryRow> InCategory(AuditCategory category) => Spec<AuditEntryRow>.Where(e => e.Category == category);

    public static Specification<AuditEntryRow> WithOutcome(AuditOutcome outcome) => Spec<AuditEntryRow>.Where(e => e.Outcome == outcome);

    public static Specification<AuditEntryRow> OfSubject(string subjectRef) => Spec<AuditEntryRow>.Where(e => e.SubjectRef == subjectRef);

    public static Specification<AuditEntryRow> ByActor(string actorRef) => Spec<AuditEntryRow>.Where(e => e.ActorRef == actorRef);

    public static Specification<AuditEntryRow> OfTenant(string tenantId) => Spec<AuditEntryRow>.Where(e => e.TenantId == tenantId);

    public static Specification<AuditEntryRow> Correlated(string correlationId) => Spec<AuditEntryRow>.Where(e => e.CorrelationId == correlationId);

    public static Specification<AuditEntryRow> OfTargetType(string targetType) => Spec<AuditEntryRow>.Where(e => e.TargetType == targetType);

    public static Specification<AuditEntryRow> OfTarget(string targetId) => Spec<AuditEntryRow>.Where(e => e.TargetId == targetId);

    /// <summary><paramref name="asked" />'s filters, the ones it sets.</summary>
    public static Specification<AuditEntryRow> Of(AuditQuery asked)
    {
        ArgumentNullException.ThrowIfNull(asked);
        return Spec<AuditEntryRow>.True
            .AndIf(asked.From is not null, () => From(asked.From!.Value.UtcTicks))
            .AndIf(asked.Until is not null, () => Until(asked.Until!.Value.UtcTicks))
            .AndIf(asked.Category is not null, () => InCategory(asked.Category!.Value))
            .AndIf(asked.Outcome is not null, () => WithOutcome(asked.Outcome!.Value))
            .AndIf(asked.SubjectRef is { Length: > 0 }, () => OfSubject(asked.SubjectRef!))
            .AndIf(asked.ActorRef is { Length: > 0 }, () => ByActor(asked.ActorRef!))
            .AndIf(asked.TenantId is { Length: > 0 }, () => OfTenant(asked.TenantId!))
            .AndIf(asked.CorrelationId is { Length: > 0 }, () => Correlated(asked.CorrelationId!))
            .AndIf(asked.TargetType is { Length: > 0 }, () => OfTargetType(asked.TargetType!))
            .AndIf(asked.TargetId is { Length: > 0 }, () => OfTarget(asked.TargetId!));
    }
}
