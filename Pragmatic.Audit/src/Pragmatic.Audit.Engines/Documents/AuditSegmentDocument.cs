using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Audit.Engines.Documents;

/// <summary>A segment of the audit trail on the documents engine (collection <c>__AuditSegments</c>): <see cref="Sql.AuditSegmentRow" />'s fields.</summary>
[Document("__AuditSegments")]
public sealed partial class AuditSegmentDocument : IAuditSegmentRecord
{
    /// <summary>The collection in a boundary's <c>{Boundary}DocSchema</c>, with the index verification walks sealed segments by.</summary>
    public static readonly DocCollectionSchema Schema = new("__AuditSegments", "/id",
        [new DocIndex("IX___AuditSegments_SealedAt", ["SealedAt"])], []);

    [Key]
    public string SegmentId { get; set; } = "";

    /// <summary>Start of the window, in UTC ticks.</summary>
    public long OpenedAt { get; set; }

    /// <summary>When the segment was sealed, in UTC ticks; null while it is open.</summary>
    public long? SealedAt { get; set; }

    public int EntryCount { get; set; }

    // The hashes in Base64: the documents engine stores no byte array.

    public string? MerkleRoot { get; set; }

    public string? PreviousHash { get; set; }

    public string? SegmentHash { get; set; }

    byte[]? IAuditSegmentRecord.MerkleRoot
    {
        get => AuditRecords.Bytes(MerkleRoot);
        set => MerkleRoot = AuditRecords.Base64(value);
    }

    byte[]? IAuditSegmentRecord.PreviousHash
    {
        get => AuditRecords.Bytes(PreviousHash);
        set => PreviousHash = AuditRecords.Base64(value);
    }

    byte[]? IAuditSegmentRecord.SegmentHash
    {
        get => AuditRecords.Bytes(SegmentHash);
        set => SegmentHash = AuditRecords.Base64(value);
    }
}
