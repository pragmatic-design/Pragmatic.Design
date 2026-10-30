using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Audit.Engines.Sql;

/// <summary>A segment of the audit trail on the Sql engine (<c>__AuditSegments</c>): EF Core's table, column for column, its instants UTC ticks.</summary>
[Entity]
[Table("__AuditSegments")]
public sealed class AuditSegmentRow : IAuditSegmentRecord
{
    [Key]
    [MaxLength(32)]
    public string SegmentId { get; set; } = "";

    /// <summary>Start of the window, in UTC ticks.</summary>
    public long OpenedAt { get; set; }

    /// <summary>When the segment was sealed, in UTC ticks; null while it is open.</summary>
    public long? SealedAt { get; set; }

    public int EntryCount { get; set; }

    public byte[]? MerkleRoot { get; set; }

    public byte[]? PreviousHash { get; set; }

    public byte[]? SegmentHash { get; set; }
}
