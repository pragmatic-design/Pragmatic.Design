using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Sql.Pragmatic;

/// <summary>
///     One entity a <see cref="SqlUnitOfWork" /> tracks: the entity, its state, and the snapshot of its columns as they were read or last saved
///     (what tells a modified entity from an unchanged one). The tenant and the soft-delete flag as they were read are kept beside it: a save
///     reverts a tenant transfer and tells a soft delete from an update, as EF Core's original values let its interceptors do.
/// </summary>
public sealed class SqlEntry : ISaveEntry
{
    private static readonly object Unknown = new(); // a column value no row holds: every column of an entity attached as modified differs from it

    internal SqlEntry(object entity, SqlEntryState state, SqlDialect dialect)
    {
        Entity = entity;
        State = state;
        if (state == SqlEntryState.Added)
            return;
        Snapshot = SqlSnapshots.Take(entity, dialect);
        OriginalTenantId = (entity as ITenantEntity)?.TenantId;
        OriginalIsDeleted = entity is ISoftDelete { IsDeleted: true };
    }

    public object Entity { get; }

    public SqlEntryState State { get; internal set; }

    /// <summary>The columns as the database holds them; null for an added entity.</summary>
    internal object?[]? Snapshot { get; private set; }

    /// <summary>The tenant the entity was read with (an <see cref="ITenantEntity" />).</summary>
    public string? OriginalTenantId { get; private set; }

    /// <summary>The soft-delete flag the entity was read with (an <see cref="ISoftDelete" />).</summary>
    public bool OriginalIsDeleted { get; private set; }

    /// <summary>A modified <see cref="ISoftDelete" /> whose flag goes from false to true: a delete, for the lifecycle events and the audit log.</summary>
    public bool IsSoftDeleting => State == SqlEntryState.Modified && Entity is ISoftDelete { IsDeleted: true } && !OriginalIsDeleted;

    /// <summary>The check that the caller may write the row, for an entity staged without being read through the unit of work; null otherwise.</summary>
    internal Func<CancellationToken, Task<bool>>? Visible { get; set; }

    bool ISaveEntry.IsAdded => State == SqlEntryState.Added;

    bool ISaveEntry.IsModified => State == SqlEntryState.Modified;

    bool ISaveEntry.IsDeleted => State == SqlEntryState.Deleted;

    void ISaveEntry.DeleteAsModified() => State = SqlEntryState.Modified;

    /// <summary>Every column as changed: an entity attached as modified (<c>Update</c> on one never read), whose stored values are not known.</summary>
    internal void ForgetSnapshot() => Snapshot = Array.ConvertAll(Snapshot!, static _ => Unknown);

    /// <summary>The columns the save writes for a modified entity.</summary>
    internal ulong ChangedColumns(SqlDialect dialect) => SqlSnapshots.ChangedColumns(Entity, Snapshot!, dialect);

    /// <summary>After a successful save: what the row now holds becomes the snapshot.</summary>
    internal void Saved(SqlDialect dialect)
    {
        State = SqlEntryState.Unchanged;
        Snapshot = SqlSnapshots.Take(Entity, dialect);
        OriginalTenantId = (Entity as ITenantEntity)?.TenantId;
        OriginalIsDeleted = Entity is ISoftDelete { IsDeleted: true };
        Visible = null;
        if (Entity is global::Pragmatic.Persistence.Entity.IChangeTracking tracking) // Pragmatic's: the enclosing namespace has the Sql engine's own
        {
            tracking.ResetModifiedProperties();
            tracking.IsNew = false;
        }
    }
}
