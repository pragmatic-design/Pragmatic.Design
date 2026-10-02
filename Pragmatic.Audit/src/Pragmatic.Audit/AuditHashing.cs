using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Audit;

/// <summary>
///     The hashes that make the trail verifiable: one per entry, a Merkle root per segment, and the link
///     that chains segments together.
/// </summary>
/// <remarks>
///     <para>
///         Every field is written length-prefixed. Concatenating fields without lengths makes the
///         encoding ambiguous — <c>("ab", "c")</c> and <c>("a", "bc")</c> would hash identically, so an
///         attacker could move characters across a field boundary without changing the hash. The
///         prefixes are what make the encoding injective.
///     </para>
///     <para>
///         Field order is fixed and must never change: reordering silently invalidates every segment
///         sealed before the change, which would look exactly like tampering. A <em>new</em> field is
///         appended at the end for the same reason — it still invalidates segments sealed earlier, so it
///         is a decision to take deliberately and before there is a trail anyone relies on, but it leaves
///         every existing position intact.
///     </para>
///     <para>
///         <b>Adding a field is a breaking change to already-sealed data.</b> <c>BusinessOperation</c>
///         and <c>OnBehalfOfRef</c> are the last two appended; segments sealed before each verify as
///         broken. Doing it later costs the same and protects less, and the
///         alternative — a field outside the hash — is one an attacker with write access could rewrite
///         while the rest stayed verifiable.
///     </para>
/// </remarks>
public static class AuditHashing
{
    /// <summary>Hashes a single entry over every field that must not change after the fact.</summary>
    public static byte[] HashEntry(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var buffer = new MemoryStream();
        WriteInt64(buffer, entry.Seq);
        WriteString(buffer, entry.SegmentId);
        WriteInt64(buffer, entry.OccurredAt.ToUnixTimeMilliseconds());
        WriteInt64(buffer, (long)entry.Category);
        WriteString(buffer, entry.Operation);
        WriteString(buffer, entry.ActorRef);
        WriteString(buffer, entry.SubjectRef);
        WriteString(buffer, entry.TenantId);
        WriteString(buffer, entry.CorrelationId);
        WriteString(buffer, entry.TargetType);
        WriteString(buffer, entry.TargetId);
        WriteInt64(buffer, (long)entry.Outcome);
        WriteBytes(buffer, entry.ValueHash);
        WriteString(buffer, entry.Detail);
        // Appended, never inserted: the fields above keep their positions, so the change is one a
        // versioned decoder could still reason about. It is inside the hash because "which operation did
        // this" is precisely what an investigation leans on — left outside, anyone able to write to the
        // table could rewrite it while every other field stayed verifiable.
        WriteString(buffer, entry.BusinessOperation);

        // Last, and inside the hash for the same reason as the field above it. "Who was this done for"
        // is the half of an investigation that a delegated call makes interesting, and a field an
        // attacker could rewrite while the rest verified would be worse than not recording it.
        WriteString(buffer, entry.OnBehalfOfRef);

        return SHA256.HashData(buffer.ToArray());
    }

    /// <summary>
    ///     Builds the Merkle root over a segment's entry hashes, in the order they were appended.
    /// </summary>
    /// <remarks>
    ///     An odd node is promoted rather than duplicated. Duplicating the last leaf is the classic
    ///     construction and carries the classic flaw: two different leaf sets can produce the same root.
    /// </remarks>
    public static byte[] MerkleRoot(IReadOnlyList<byte[]> leafHashes)
    {
        ArgumentNullException.ThrowIfNull(leafHashes);

        if (leafHashes.Count == 0)
            return SHA256.HashData([]);

        var level = leafHashes.ToArray();

        while (level.Length > 1)
        {
            var next = new byte[(level.Length + 1) / 2][];

            for (var i = 0; i < level.Length; i += 2)
            {
                if (i + 1 == level.Length)
                {
                    next[i / 2] = level[i];   // promoted, not duplicated
                    continue;
                }

                var pair = new byte[level[i].Length + level[i + 1].Length];
                level[i].CopyTo(pair, 0);
                level[i + 1].CopyTo(pair, level[i].Length);
                next[i / 2] = SHA256.HashData(pair);
            }

            level = next;
        }

        return level[0];
    }

    /// <summary>Hashes a sealed segment into the chain: <c>H(previousHash || merkleRoot || segmentId)</c>.</summary>
    public static byte[] HashSegment(byte[]? previousHash, byte[] merkleRoot, string segmentId)
    {
        ArgumentNullException.ThrowIfNull(merkleRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);

        using var buffer = new MemoryStream();
        WriteBytes(buffer, previousHash);
        WriteBytes(buffer, merkleRoot);
        WriteString(buffer, segmentId);

        return SHA256.HashData(buffer.ToArray());
    }

    private static void WriteInt64(Stream to, long value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, value);
        to.Write(b);
    }

    private static void WriteBytes(Stream to, byte[]? value)
    {
        // -1 for absent, so a null field and an empty one cannot collide.
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, value?.Length ?? -1);
        to.Write(len);

        if (value is { Length: > 0 })
            to.Write(value);
    }

    private static void WriteString(Stream to, string? value)
        => WriteBytes(to, value is null ? null : Encoding.UTF8.GetBytes(value));
}
