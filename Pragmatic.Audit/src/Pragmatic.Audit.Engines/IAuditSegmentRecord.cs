namespace Pragmatic.Audit.Engines;

/// <summary>A segment of the trail as both engines store it: <see cref="AuditSegment" />'s fields, its instants in UTC ticks.</summary>
internal interface IAuditSegmentRecord
{
    string SegmentId { get; set; }

    long OpenedAt { get; set; }

    long? SealedAt { get; set; }

    int EntryCount { get; set; }

    byte[]? MerkleRoot { get; set; }

    byte[]? PreviousHash { get; set; }

    byte[]? SegmentHash { get; set; }
}
