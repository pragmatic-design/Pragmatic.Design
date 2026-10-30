using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Audit.Engines.Sql;

/// <summary>
///     An entry of the audit trail on the Sql engine (<c>__AuditEntries</c>): EF Core's table, column for column, its instants UTC ticks, so a
///     database holds one trail whichever writes it. <see cref="Seq" /> is the database's identity, as there.
/// </summary>
[Entity]
[Table("__AuditEntries")]
public sealed class AuditEntryRow : IAuditEntryRecord
{
    /// <summary>The database's order of the entries: sealing hashes a segment's entries by it.</summary>
    [Key]
    [Identity]
    public long Seq { get; set; }

    [MaxLength(32)]
    public string SegmentId { get; set; } = "";

    /// <summary>When it happened, in UTC ticks.</summary>
    public long OccurredAt { get; set; }

    public AuditCategory Category { get; set; }

    [MaxLength(128)]
    public string Operation { get; set; } = "";

    [MaxLength(128)]
    public string? ActorRef { get; set; }

    [MaxLength(128)]
    public string? SubjectRef { get; set; }

    [MaxLength(128)]
    public string? TenantId { get; set; }

    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    [MaxLength(256)]
    public string? BusinessOperation { get; set; }

    [MaxLength(128)]
    public string? OnBehalfOfRef { get; set; }

    [MaxLength(256)]
    public string? TargetType { get; set; }

    [MaxLength(128)]
    public string? TargetId { get; set; }

    public AuditOutcome Outcome { get; set; }

    public byte[]? ValueHash { get; set; }

    [MaxLength(2048)]
    public string? Detail { get; set; }
}
