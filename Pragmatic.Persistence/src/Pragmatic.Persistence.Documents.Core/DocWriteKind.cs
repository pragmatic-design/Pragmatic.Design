namespace Pragmatic.Persistence.Documents;

/// <summary>How a <see cref="DocWrittenDocument" /> is written.</summary>
public enum DocWriteKind
{
    /// <summary>A new document.</summary>
    Insert,

    /// <summary>A document changed: its fields set, or replaced whole, or upserted (inserted or replaced).</summary>
    Update,

    /// <summary>A document removed.</summary>
    Delete,
}
