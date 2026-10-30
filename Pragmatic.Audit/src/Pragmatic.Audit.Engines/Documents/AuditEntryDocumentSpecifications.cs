using Pragmatic.Specification;

namespace Pragmatic.Audit.Engines.Documents;

/// <summary>The reader's filters on the documents engine, one per field of <see cref="AuditQuery" />, composed at runtime: what is not asked for is not filtered.</summary>
public static partial class AuditEntryDocumentSpecifications
{
    public static Specification<AuditEntryDocument> From(long ticks) => Spec<AuditEntryDocument>.Where(e => e.OccurredAt >= ticks);

    public static Specification<AuditEntryDocument> Until(long ticks) => Spec<AuditEntryDocument>.Where(e => e.OccurredAt < ticks);

    public static Specification<AuditEntryDocument> InCategory(AuditCategory category) => Spec<AuditEntryDocument>.Where(e => e.Category == category);

    public static Specification<AuditEntryDocument> WithOutcome(AuditOutcome outcome) => Spec<AuditEntryDocument>.Where(e => e.Outcome == outcome);

    public static Specification<AuditEntryDocument> OfSubject(string subjectRef) => Spec<AuditEntryDocument>.Where(e => e.SubjectRef == subjectRef);

    public static Specification<AuditEntryDocument> ByActor(string actorRef) => Spec<AuditEntryDocument>.Where(e => e.ActorRef == actorRef);

    public static Specification<AuditEntryDocument> OfTenant(string tenantId) => Spec<AuditEntryDocument>.Where(e => e.TenantId == tenantId);

    public static Specification<AuditEntryDocument> Correlated(string correlationId) => Spec<AuditEntryDocument>.Where(e => e.CorrelationId == correlationId);

    public static Specification<AuditEntryDocument> OfTargetType(string targetType) => Spec<AuditEntryDocument>.Where(e => e.TargetType == targetType);

    public static Specification<AuditEntryDocument> OfTarget(string targetId) => Spec<AuditEntryDocument>.Where(e => e.TargetId == targetId);

    /// <summary><paramref name="asked" />'s filters, the ones it sets.</summary>
    public static Specification<AuditEntryDocument> Of(AuditQuery asked)
    {
        ArgumentNullException.ThrowIfNull(asked);
        return Spec<AuditEntryDocument>.True
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
