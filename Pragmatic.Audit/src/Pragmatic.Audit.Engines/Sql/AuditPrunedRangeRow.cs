using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Audit.Engines.Sql;

/// <summary>
///     A gap retention left in the audit trail on the Sql engine (<c>__AuditPrunedRanges</c>): EF Core's table, column for column. It must
///     outlive the segments it describes: deleting it would turn a declared gap back into an unexplained one.
/// </summary>
[Entity]
[Table("__AuditPrunedRanges")]
public sealed class AuditPrunedRangeRow : IAuditPrunedRangeRecord
{
    [Key]
    [MaxLength(32)]
    public string FromSegmentId { get; set; } = "";

    [MaxLength(32)]
    public string UntilSegmentId { get; set; } = "";

    /// <summary>When the segments were removed, in UTC ticks.</summary>
    public long PrunedAt { get; set; }

    /// <summary>The hash the segment after the gap still links to.</summary>
    public byte[] LinkHash { get; set; } = [];
}
