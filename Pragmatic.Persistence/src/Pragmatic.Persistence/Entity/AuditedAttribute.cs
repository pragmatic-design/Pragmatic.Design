namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks an entity for change auditing. The source generator makes it implement
///     <see cref="IAuditedEntity"/>; the <c>AuditLogInterceptor</c> then records an append-only audit
///     entry — entity type + id, action (Created/Updated/Deleted), user, timestamp and correlation id —
///     to the boundary's audit log on each persisted change, within the same transaction.
/// </summary>
/// <remarks>
///     This is a change log (history of who did what), distinct from <c>[Auditable]</c> which stamps
///     CreatedAt/UpdatedAt/By on the row itself. A near-zero-cost "event sourcing-light" for audit and
///     troubleshooting without converting the model to event sourcing.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AuditedAttribute : Attribute;
