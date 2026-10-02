using Pragmatic.Persistence.Entity;

namespace Pragmatic.Actions.Invoker;

public abstract partial class MutationInvoker<TMutation, TEntity>
{
    // =========================================================================
    // Soft-Delete Compensation
    // =========================================================================

    /// <summary>
    ///     Captures soft-delete state for compensation if <see cref="SaveChangesAsync"/> fails.
    ///     Returns null if entity does not implement <see cref="ISoftDelete"/>.
    /// </summary>
    private static SoftDeleteSnapshot? CaptureSoftDeleteState(TEntity entity)
    {
        if (entity is not ISoftDelete softDelete)
            return null;
        return new SoftDeleteSnapshot(softDelete.IsDeleted, softDelete.DeletedAt, softDelete.DeletedBy);
    }

    /// <summary>
    ///     Restores soft-delete state from a snapshot captured before mutation.
    ///     Also calls <see cref="CompensateSoftDeleteCascade"/> for cascade targets.
    /// </summary>
    private void RestoreSoftDeleteState(TEntity entity, SoftDeleteSnapshot? snapshot)
    {
        if (snapshot is null || entity is not ISoftDelete softDelete)
            return;
        softDelete.IsDeleted = snapshot.IsDeleted;
        softDelete.DeletedAt = snapshot.DeletedAt;
        softDelete.DeletedBy = snapshot.DeletedBy;
        CompensateSoftDeleteCascade(entity);
    }

    /// <summary>
    ///     Compensates cascade targets after a failed <see cref="SaveChangesAsync"/>.
    ///     Override in generated classes that have <c>[SoftDelete(Cascade = true)]</c>.
    /// </summary>
    protected virtual void CompensateSoftDeleteCascade(TEntity entity) { }

    /// <summary>
    ///     Deletes the entity (hard delete by default).
    ///     Override in generated classes for soft delete behavior.
    /// </summary>
    protected abstract void DeleteEntity(TEntity entity);

    /// <summary>
    ///     Restores a soft-deleted entity by resetting ISoftDelete fields.
    ///     Default: no-op. Override in generated classes for restore behavior.
    /// </summary>
    protected virtual void RestoreEntity(TEntity entity) { }

    /// <summary>
    ///     Snapshot of ISoftDelete state for compensation on persist failure.
    /// </summary>
    private sealed record SoftDeleteSnapshot(bool IsDeleted, DateTimeOffset? DeletedAt, string? DeletedBy);
}
