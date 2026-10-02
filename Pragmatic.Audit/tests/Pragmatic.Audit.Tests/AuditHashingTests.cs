using Pragmatic.Testing.Assertions;

namespace Pragmatic.Audit.Tests;

/// <summary>
///     Covers the hashes the trail's integrity rests on: an entry hash that is sensitive to every field
///     it covers, an encoding that cannot be gamed by moving characters across field boundaries, and a
///     Merkle root that depends on order.
/// </summary>
public sealed class AuditHashingTests
{
    private static AuditEntry Entry(Action<AuditEntry>? tweak = null)
    {
        var e = new AuditEntry
        {
            Seq = 1,
            SegmentId = "2026-07-30T14",
            OccurredAt = DateTimeOffset.UnixEpoch,
            Category = AuditCategory.Data,
            Operation = "Order.Updated",
            ActorRef = "actor-1",
            SubjectRef = "subject-1",
            TenantId = "tenant-1",
            CorrelationId = "corr-1",
            TargetType = "Order",
            TargetId = "42",
            Outcome = AuditOutcome.Success,
            ValueHash = [1, 2, 3],
            Detail = "changed",
            BusinessOperation = "App.UpdateOrderMutation",
            OnBehalfOfRef = "principal-1"
        };
        tweak?.Invoke(e);
        return e;
    }

    [Fact]
    public void HashEntry_IsDeterministic()
        => AuditHashing.HashEntry(Entry()).Should().Equal(AuditHashing.HashEntry(Entry()));

    [Theory]
    [InlineData("Seq")]
    [InlineData("Operation")]
    [InlineData("ActorRef")]
    [InlineData("SubjectRef")]
    [InlineData("TenantId")]
    [InlineData("CorrelationId")]
    [InlineData("TargetType")]
    [InlineData("TargetId")]
    [InlineData("Outcome")]
    [InlineData("ValueHash")]
    [InlineData("Detail")]
    [InlineData("OccurredAt")]
    [InlineData("Category")]
    [InlineData("SegmentId")]
    // Which operation did this is what an investigation leans on, so it is covered like the rest.
    // Outside the hash it would be the one field anyone able to write to the table could rewrite while
    // every other stayed verifiable.
    [InlineData("BusinessOperation")]
    // Who the actor was working for. Outside the hash it would be the one field that could be rewritten
    // to make a delegated change look like an ordinary one, while every other field still verified.
    [InlineData("OnBehalfOfRef")]
    public void HashEntry_ChangesWhenAnyCoveredFieldChanges(string field)
    {
        // A field the hash does not cover is a field an attacker can rewrite for free.
        var baseline = AuditHashing.HashEntry(Entry());

        var altered = AuditHashing.HashEntry(Entry(e =>
        {
            switch (field)
            {
                case "Seq": e.Seq = 2; break;
                case "Operation": e.Operation = "Order.Deleted"; break;
                case "ActorRef": e.ActorRef = "actor-2"; break;
                case "SubjectRef": e.SubjectRef = "subject-2"; break;
                case "TenantId": e.TenantId = "tenant-2"; break;
                case "CorrelationId": e.CorrelationId = "corr-2"; break;
                case "TargetType": e.TargetType = "Invoice"; break;
                case "TargetId": e.TargetId = "43"; break;
                case "Outcome": e.Outcome = AuditOutcome.Denied; break;
                case "ValueHash": e.ValueHash = [1, 2, 4]; break;
                case "Detail": e.Detail = "changed differently"; break;
                case "OccurredAt": e.OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(1); break;
                case "Category": e.Category = AuditCategory.Security; break;
                case "SegmentId": e.SegmentId = "2026-07-30T15"; break;
                case "BusinessOperation": e.BusinessOperation = "App.OtherMutation"; break;
                case "OnBehalfOfRef": e.OnBehalfOfRef = "principal-2"; break;
            }
        }));

        altered.Should().NotEqual(baseline);
    }

    [Fact]
    public void HashEntry_CannotBeGamedByMovingCharactersAcrossFields()
    {
        // Without length prefixes, ("ab","c") and ("a","bc") concatenate identically — an attacker
        // could shift a character from one field to the next and keep the hash.
        var a = AuditHashing.HashEntry(Entry(e => { e.ActorRef = "ab"; e.SubjectRef = "c"; }));
        var b = AuditHashing.HashEntry(Entry(e => { e.ActorRef = "a"; e.SubjectRef = "bc"; }));

        a.Should().NotEqual(b);
    }

    [Fact]
    public void HashEntry_TellsNullApartFromEmpty()
    {
        var withNull = AuditHashing.HashEntry(Entry(e => e.Detail = null));
        var withEmpty = AuditHashing.HashEntry(Entry(e => e.Detail = string.Empty));

        withNull.Should().NotEqual(withEmpty);
    }

    // =========================================================================
    // Merkle root
    // =========================================================================

    private static byte[] Leaf(byte b) => [b, b, b, b];

    [Fact]
    public void MerkleRoot_SingleLeaf_IsThatLeaf()
        => AuditHashing.MerkleRoot([Leaf(1)]).Should().Equal(Leaf(1));

    [Fact]
    public void MerkleRoot_DependsOnOrder()
    {
        var ab = AuditHashing.MerkleRoot([Leaf(1), Leaf(2)]);
        var ba = AuditHashing.MerkleRoot([Leaf(2), Leaf(1)]);

        ab.Should().NotEqual(ba, "reordering entries must be detectable");
    }

    [Fact]
    public void MerkleRoot_ChangesWhenAnyLeafChanges()
    {
        var baseline = AuditHashing.MerkleRoot([Leaf(1), Leaf(2), Leaf(3)]);
        var altered = AuditHashing.MerkleRoot([Leaf(1), Leaf(9), Leaf(3)]);

        altered.Should().NotEqual(baseline);
    }

    [Fact]
    public void MerkleRoot_OddLeafCount_PromotesInsteadOfDuplicating()
    {
        // Duplicating the last leaf is the classic construction and the classic flaw: it makes
        // [a, b, b] and [a, b] produce the same root, so an entry can be added without trace.
        var three = AuditHashing.MerkleRoot([Leaf(1), Leaf(2), Leaf(2)]);
        var two = AuditHashing.MerkleRoot([Leaf(1), Leaf(2)]);

        three.Should().NotEqual(two);
    }

    [Fact]
    public void MerkleRoot_IsDeterministic()
    {
        var leaves = new[] { Leaf(1), Leaf(2), Leaf(3), Leaf(4), Leaf(5) };

        AuditHashing.MerkleRoot(leaves).Should().Equal(AuditHashing.MerkleRoot(leaves));
    }

    // =========================================================================
    // Segment chain
    // =========================================================================

    [Fact]
    public void HashSegment_DependsOnThePreviousHash()
    {
        var root = AuditHashing.MerkleRoot([Leaf(1)]);

        var first = AuditHashing.HashSegment(null, root, "seg-1");
        var linked = AuditHashing.HashSegment(Leaf(9), root, "seg-1");

        linked.Should().NotEqual(first, "a segment must be bound to its predecessor");
    }

    [Fact]
    public void HashSegment_DependsOnTheSegmentId()
    {
        var root = AuditHashing.MerkleRoot([Leaf(1)]);

        AuditHashing.HashSegment(Leaf(9), root, "seg-1")
            .Should().NotEqual(AuditHashing.HashSegment(Leaf(9), root, "seg-2"));
    }
}
