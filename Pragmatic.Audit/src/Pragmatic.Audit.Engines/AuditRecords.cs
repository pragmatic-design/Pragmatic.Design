namespace Pragmatic.Audit.Engines;

/// <summary>Between <c>Pragmatic.Audit</c>'s entry, segment and gap and the rows or documents that store them: instants in UTC ticks.</summary>
internal static class AuditRecords
{
    /// <summary><paramref name="record" />, filled from a prepared <paramref name="entry" /> (its <c>Seq</c> is the store's to assign).</summary>
    public static T From<T>(T record, AuditEntry entry) where T : IAuditEntryRecord
    {
        record.SegmentId = entry.SegmentId;
        record.OccurredAt = entry.OccurredAt.UtcTicks;
        record.Category = entry.Category;
        record.Operation = entry.Operation;
        record.ActorRef = entry.ActorRef;
        record.SubjectRef = entry.SubjectRef;
        record.TenantId = entry.TenantId;
        record.CorrelationId = entry.CorrelationId;
        record.BusinessOperation = entry.BusinessOperation;
        record.OnBehalfOfRef = entry.OnBehalfOfRef;
        record.TargetType = entry.TargetType;
        record.TargetId = entry.TargetId;
        record.Outcome = entry.Outcome;
        record.ValueHash = entry.ValueHash;
        record.Detail = entry.Detail;
        return record;
    }

    /// <summary>The entry <paramref name="record" /> stores.</summary>
    public static AuditEntry ToEntry(IAuditEntryRecord record) => new()
    {
        Seq = record.Seq,
        SegmentId = record.SegmentId,
        OccurredAt = Instant(record.OccurredAt),
        Category = record.Category,
        Operation = record.Operation,
        ActorRef = record.ActorRef,
        SubjectRef = record.SubjectRef,
        TenantId = record.TenantId,
        CorrelationId = record.CorrelationId,
        BusinessOperation = record.BusinessOperation,
        OnBehalfOfRef = record.OnBehalfOfRef,
        TargetType = record.TargetType,
        TargetId = record.TargetId,
        Outcome = record.Outcome,
        ValueHash = record.ValueHash,
        Detail = record.Detail,
    };

    /// <summary>The segment <paramref name="record" /> stores.</summary>
    public static AuditSegment ToSegment(IAuditSegmentRecord record) => new()
    {
        SegmentId = record.SegmentId,
        OpenedAt = Instant(record.OpenedAt),
        SealedAt = record.SealedAt is { } sealedAt ? Instant(sealedAt) : null,
        EntryCount = record.EntryCount,
        MerkleRoot = record.MerkleRoot,
        PreviousHash = record.PreviousHash,
        SegmentHash = record.SegmentHash,
    };

    /// <summary><paramref name="record" />, its seal taken from <paramref name="segment" /> (<see cref="AuditChain.Seal" />).</summary>
    public static T Sealed<T>(T record, AuditSegment segment) where T : IAuditSegmentRecord
    {
        record.SealedAt = segment.SealedAt?.UtcTicks;
        record.EntryCount = segment.EntryCount;
        record.MerkleRoot = segment.MerkleRoot;
        record.PreviousHash = segment.PreviousHash;
        record.SegmentHash = segment.SegmentHash;
        return record;
    }

    /// <summary>The gap <paramref name="record" /> declares.</summary>
    public static PrunedRange ToRange(IAuditPrunedRangeRecord record)
        => new(record.FromSegmentId, record.UntilSegmentId, Instant(record.PrunedAt), record.LinkHash);

    /// <summary><paramref name="record" />, filled from <paramref name="range" />.</summary>
    public static T From<T>(T record, PrunedRange range) where T : IAuditPrunedRangeRecord
    {
        record.FromSegmentId = range.FromSegmentId;
        record.UntilSegmentId = range.UntilSegmentId;
        record.PrunedAt = range.PrunedAt.UtcTicks;
        record.LinkHash = range.LinkHash;
        return record;
    }

    public static DateTimeOffset Instant(long ticks) => new(ticks, TimeSpan.Zero);

    /// <summary>A hash a document stores in Base64, as bytes.</summary>
    public static byte[]? Bytes(string? base64) => base64 is null ? null : Convert.FromBase64String(base64);

    /// <summary>A hash in the Base64 a document stores.</summary>
    public static string? Base64(byte[]? bytes) => bytes is null ? null : Convert.ToBase64String(bytes);
}
