using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Engines;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Documents.Pragmatic;

/// <summary>
///     One entity a <see cref="DocUnitOfWork" /> tracks: the entity, its state, the snapshot of its fields as they were read or last saved (what
///     tells a modified document from an unchanged one, and which fields a MongoDB <c>$set</c> writes), and the tenant and soft-delete flag as
///     they were read, as <c>SqlEntry</c> keeps them.
/// </summary>
public sealed class DocEntry : ISaveEntry
{
    internal DocEntry(object entity, DocEntryState state)
    {
        Entity = entity;
        State = state;
        Tracker = DocTrackers.For(entity.GetType());
        if (state == DocEntryState.Added)
            return;
        Snapshot = Tracker.Snapshot(entity);
        OriginalTenantId = (entity as ITenantEntity)?.TenantId;
        OriginalIsDeleted = entity is ISoftDelete { IsDeleted: true };
    }

    public object Entity { get; }

    public DocEntryState State { get; internal set; }

    internal DocTracker Tracker { get; }

    /// <summary>The fields as the database holds them; null for an added entity.</summary>
    internal object?[]? Snapshot { get; private set; }

    /// <summary>The tenant the entity was read with (an <see cref="ITenantEntity" />).</summary>
    public string? OriginalTenantId { get; private set; }

    /// <summary>The soft-delete flag the entity was read with (an <see cref="ISoftDelete" />).</summary>
    public bool OriginalIsDeleted { get; private set; }

    /// <summary>A modified <see cref="ISoftDelete" /> whose flag goes from false to true: a delete, for the lifecycle events and the audit log.</summary>
    public bool IsSoftDeleting => State == DocEntryState.Modified && Entity is ISoftDelete { IsDeleted: true } && !OriginalIsDeleted;

    /// <summary>The check that the caller may write the document, for an entity staged without being read through the unit of work; null otherwise.</summary>
    internal Func<CancellationToken, Task<bool>>? Visible { get; set; }

    bool ISaveEntry.IsAdded => State == DocEntryState.Added;

    bool ISaveEntry.IsModified => State == DocEntryState.Modified;

    bool ISaveEntry.IsDeleted => State == DocEntryState.Deleted;

    void ISaveEntry.DeleteAsModified() => State = DocEntryState.Modified;

    /// <summary>Every field as changed: an entity attached as modified (<c>Update</c> on one never read), whose stored values are not known.</summary>
    internal void ForgetSnapshot() => Snapshot = null;

    /// <summary>The fields the save writes for a modified entity: all of them when the snapshot is not known.</summary>
    internal ulong ChangedFields() => Snapshot is null ? ulong.MaxValue : Tracker.Changed(Entity, Snapshot);

    /// <summary>After a successful save: what the document now holds becomes the snapshot.</summary>
    internal void Saved()
    {
        State = DocEntryState.Unchanged;
        Snapshot = Tracker.Snapshot(Entity);
        OriginalTenantId = (Entity as ITenantEntity)?.TenantId;
        OriginalIsDeleted = Entity is ISoftDelete { IsDeleted: true };
        Visible = null;
        if (Entity is IChangeTracking tracking)
        {
            tracking.ResetModifiedProperties();
            tracking.IsNew = false;
        }
    }
}
