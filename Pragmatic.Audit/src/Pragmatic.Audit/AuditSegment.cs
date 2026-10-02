namespace Pragmatic.Audit;

/// <summary>
///     A window of entries that gets sealed as a unit.
/// </summary>
/// <remarks>
///     <para>
///         <b>The chain links segments, not entries.</b> Hashing each entry to its predecessor is the
///         obvious design and the wrong one: it needs a total order over every write in the system,
///         which means serialising them, which makes the trail the bottleneck of everything that
///         records anything.
///     </para>
///     <para>
///         Entries are appended freely into the open segment. A periodic job closes it, computes a
///         Merkle root over its entries, and links that root to the previous segment's hash. Tampering
///         with any single entry changes the root; the chain fixes the order of the segments.
///     </para>
///     <para>
///         This is tamper-<b>evident</b>, not tamper-<b>proof</b>. Anyone with write access to the
///         database can still alter a row — verification reveals it, nothing here prevents it.
///         Prevention needs database permissions or WORM storage, which are deployment concerns.
///     </para>
/// </remarks>
public sealed class AuditSegment
{
    /// <summary>Identifier of the segment, stable and sortable.</summary>
    public required string SegmentId { get; set; }

    /// <summary>Start of the window.</summary>
    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>When the segment was sealed; <see langword="null" /> while it is still open.</summary>
    public DateTimeOffset? SealedAt { get; set; }

    /// <summary>Number of entries sealed into the segment.</summary>
    public int EntryCount { get; set; }

    /// <summary>Merkle root over the segment's entries. Set at sealing.</summary>
    public byte[]? MerkleRoot { get; set; }

    /// <summary>Hash of the previous segment, linking this one into the chain.</summary>
    public byte[]? PreviousHash { get; set; }

    /// <summary>This segment's own hash: <c>H(previousHash || merkleRoot || segmentId)</c>.</summary>
    public byte[]? SegmentHash { get; set; }

    /// <summary>True once the segment is closed and hashed.</summary>
    public bool IsSealed => SealedAt is not null;
}
