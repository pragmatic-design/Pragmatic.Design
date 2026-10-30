namespace Pragmatic.Audit.Engines;

/// <summary>What both engines' stores say the same way EF Core's does.</summary>
internal static class AuditStore
{
    /// <summary>Most entries a reader returns at once, so a query cannot pull the whole trail into memory: EF Core's reader's bound.</summary>
    public const int MaxLimit = 1000;

    /// <summary>
    ///     An entry refused because its segment is sealed. Refused rather than absorbed: it would sit outside the Merkle root, and the next
    ///     verification would report it as tampering, so the failure surfaces here, where it can be understood.
    /// </summary>
    public static InvalidOperationException SealedSegment(AuditEntry entry) => new(
        $"Segment '{entry.SegmentId}' is already sealed; an entry timestamped {entry.OccurredAt:O} " +
        "cannot be appended to it. This means a write outlived the sealing grace period.");
}
