namespace Pragmatic.Persistence.Repository;

/// <summary>How a <see cref="CommitEntry" /> is written.</summary>
public enum CommitEntryState
{
    /// <summary>A new row.</summary>
    Added,

    /// <summary>A row changed: an update, a soft delete or a restore, or an upsert, which inserts or updates.</summary>
    Modified,

    /// <summary>A row removed.</summary>
    Deleted,
}
