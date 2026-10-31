using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Audit.Engines.Documents;

/// <summary>
///     An entry of the audit trail on the documents engine (collection <c>__AuditEntries</c>): <see cref="Sql.AuditEntryRow" />'s fields, its
///     instant in UTC ticks. The documents engine has no identity, so <see cref="Seq" /> is the entry's instant in ticks and <see cref="Id" />
///     the tie-break: a segment's entries are sealed and verified in <c>(Seq, Id)</c> order (owner's decision), with no writer coordinating
///     with another.
/// </summary>
[Document("__AuditEntries")]
public sealed partial class AuditEntryDocument : IAuditEntryRecord
{
    /// <summary>
    ///     The collection in a boundary's <c>{Boundary}DocSchema</c>: the indexes sealing (a segment's entries in order) and the queries by time
    ///     read, named as EF Core's on <c>__AuditEntries</c>.
    /// </summary>
    public static readonly DocCollectionSchema Schema = new("__AuditEntries", "/id",
    [
        new DocIndex("IX___AuditEntries_SegmentId_Seq_Id", ["SegmentId", "Seq", "Id"]),
        new DocIndex("IX___AuditEntries_OccurredAt", ["OccurredAt"]),
    ], []);

    [Key]
    public Guid Id { get; set; }

    /// <summary>The entry's instant in UTC ticks: with <see cref="Id" />, the order sealing hashes a segment's entries in.</summary>
    public long Seq { get; set; }

    public string SegmentId { get; set; } = "";

    /// <summary>When it happened, in UTC ticks.</summary>
    public long OccurredAt { get; set; }

    public AuditCategory Category { get; set; }

    public string Operation { get; set; } = "";

    public string? ActorRef { get; set; }

    public string? SubjectRef { get; set; }

    public string? TenantId { get; set; }

    public string? CorrelationId { get; set; }

    public string? BusinessOperation { get; set; }

    public string? OnBehalfOfRef { get; set; }

    public string? TargetType { get; set; }

    public string? TargetId { get; set; }

    public AuditOutcome Outcome { get; set; }

    /// <summary>The hash of the previous value, in Base64: the documents engine stores no byte array.</summary>
    public string? ValueHash { get; set; }

    public string? Detail { get; set; }

    byte[]? IAuditEntryRecord.ValueHash
    {
        get => AuditRecords.Bytes(ValueHash);
        set => ValueHash = AuditRecords.Base64(value);
    }
}
