using Pragmatic.Persistence.Entity;

namespace Pragmatic.Audit.Engines;

/// <summary>What both engines' phases read off an entry of the save: whether it is audited, the operation it records, the key of its target.</summary>
internal static class AuditedEntries
{
    /// <summary>
    ///     The operation a write to <paramref name="entity" /> records, as EF Core's interceptor names it; null when the entity is not
    ///     <c>[Audited]</c>. A soft delete is a delete and a restore a restore: the trail records the domain's meaning, not the store's.
    /// </summary>
    public static string? OperationOf(object entity, bool added, bool deleted, bool modified, bool originalIsDeleted)
    {
        if (entity is not IAuditedEntity)
            return null;
        if (added)
            return AuditedChange.EntityCreated;
        if (deleted)
            return AuditedChange.EntityDeleted;
        if (!modified)
            return null;
        var isDeleted = entity is ISoftDelete { IsDeleted: true };
        return isDeleted && !originalIsDeleted ? AuditedChange.EntityDeleted
            : !isDeleted && originalIsDeleted ? AuditedChange.EntityRestored
            : AuditedChange.EntityUpdated;
    }

    /// <summary>The entity's key, as EF Core's interceptor writes a client-assigned one.</summary>
    public static string TargetIdOf(object entity) => entity is IEntity keyed ? keyed.PersistenceId.ToString() : "";
}
