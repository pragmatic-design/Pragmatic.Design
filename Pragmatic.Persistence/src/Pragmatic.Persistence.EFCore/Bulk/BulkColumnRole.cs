namespace Pragmatic.Persistence.EFCore.Bulk;

/// <summary>
///     Role of a column in bulk operations, determining how it's handled in INSERT and UPSERT.
/// </summary>
public enum BulkColumnRole
{
    /// <summary>Regular column — included in INSERT values and UPSERT SET clause.</summary>
    Regular,

    /// <summary>Primary key — used as match key, included in INSERT but not in UPSERT SET.</summary>
    Key,

    /// <summary>Logic key ([LogicKey]) — alternative match key, included in INSERT but not in UPSERT SET.</summary>
    LogicKey,

    /// <summary>Set on insert only (CreatedAt, CreatedBy) — not updated on upsert match.</summary>
    InsertOnly,

    /// <summary>Set on update only (UpdatedAt, UpdatedBy) — not included in initial insert.</summary>
    UpdateOnly,

    /// <summary>Soft-delete columns (IsDeleted, DeletedAt, DeletedBy) — initialized on insert, not touched on upsert match.</summary>
    SoftDelete,

    /// <summary>Computed/auto-generated (RowVersion) — skipped entirely in both INSERT and UPSERT.</summary>
    Computed,
}
