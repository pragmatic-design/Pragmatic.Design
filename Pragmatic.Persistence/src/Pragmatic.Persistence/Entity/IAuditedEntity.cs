namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marker for entities decorated with <c>[Audited]</c>. The source generator makes such entities
///     implement this interface; the <c>AuditLogInterceptor</c> writes an append-only audit record
///     (who/what/when + correlation id) to the boundary's audit log on every insert/update/delete,
///     in the same transaction as the change.
/// </summary>
public interface IAuditedEntity;
