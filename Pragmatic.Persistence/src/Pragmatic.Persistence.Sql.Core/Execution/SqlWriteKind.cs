namespace Pragmatic.Persistence.Sql.Execution;

/// <summary>How a <see cref="SqlWrittenRow" /> is written.</summary>
public enum SqlWriteKind
{
    /// <summary>A new row.</summary>
    Insert,

    /// <summary>A row changed: an update, a soft delete or a restore, or an upsert, which inserts or updates.</summary>
    Update,

    /// <summary>A row removed.</summary>
    Delete,
}
