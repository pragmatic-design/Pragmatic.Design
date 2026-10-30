using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Audit.Engines.Documents;

/// <summary>
///     A gap retention left in the audit trail on the documents engine (collection <c>__AuditPrunedRanges</c>): <see cref="Sql.AuditPrunedRangeRow" />'s
///     fields. It must outlive the segments it describes.
/// </summary>
[Document("__AuditPrunedRanges")]
public sealed partial class AuditPrunedRangeDocument : IAuditPrunedRangeRecord
{
    /// <summary>The collection in a boundary's <c>{Boundary}DocSchema</c>.</summary>
    public static readonly DocCollectionSchema Schema = new("__AuditPrunedRanges", "/id", [], []);

    [Key]
    public string FromSegmentId { get; set; } = "";

    public string UntilSegmentId { get; set; } = "";

    /// <summary>When the segments were removed, in UTC ticks.</summary>
    public long PrunedAt { get; set; }

    /// <summary>The hash the segment after the gap still links to, in Base64: the documents engine stores no byte array.</summary>
    public string LinkHash { get; set; } = "";

    byte[] IAuditPrunedRangeRecord.LinkHash
    {
        get => Convert.FromBase64String(LinkHash);
        set => LinkHash = Convert.ToBase64String(value);
    }
}
