using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Audit.EFCore.Tests;

/// <summary>
///     Verification stepping over a gap that retention declared.
/// </summary>
/// <remarks>
///     <para>
///         This state is built by hand because the retention service cannot produce it: it prunes from
///         the oldest segment forward, so the gap always sits <em>before</em> the first surviving
///         segment — and the first segment of a range never has its link checked, since it legitimately
///         links to something outside the range.
///     </para>
///     <para>
///         The branch still matters. A partial verification range, or a gap left by anything other than
///         the retention service, produces exactly this shape: a segment whose predecessor is gone and
///         whose link points at the hash the declaration carries. Without the branch that reads as a
///         broken chain, which is the one thing verification must not say about a documented removal.
///     </para>
/// </remarks>
public class DeclaredGapVerificationTests
{
    private static async Task<AuditSegment[]> TwoSealedSegmentsAsync(AuditTrailFixture f)
    {
        await f.Trail.RecordAsync(f.Entry("First"));
        f.Clock.Advance(TimeSpan.FromHours(1) + f.Naming.GracePeriod + TimeSpan.FromMinutes(1));
        await f.Sealing.SealDueSegmentsAsync();

        await f.Trail.RecordAsync(f.Entry("Second", f.Clock.GetUtcNow()));
        f.Clock.Advance(TimeSpan.FromHours(1) + f.Naming.GracePeriod + TimeSpan.FromMinutes(1));
        await f.Sealing.SealDueSegmentsAsync();

        f.Db.ChangeTracker.Clear();
        return await f.Db.Segments.OrderBy(s => s.SegmentId).ToArrayAsync();
    }

    [Fact]
    public async Task ASegmentLinkingAcrossADeclaredGap_StillVerifies()
    {
        using var f = new AuditTrailFixture();
        var segments = await TwoSealedSegmentsAsync(f);
        segments.Should().HaveCount(2);

        // Point the second segment at a hash that is not its predecessor's, and declare a gap carrying
        // that hash — the shape a removal leaves behind.
        byte[] acrossTheGap = [9, 9, 9, 9];
        var second = await f.Db.Segments.SingleAsync(s => s.SegmentId == segments[1].SegmentId);
        second.PreviousHash = acrossTheGap;
        second.SegmentHash = AuditHashing.HashSegment(acrossTheGap, second.MerkleRoot!, second.SegmentId);

        f.Db.PrunedRanges.Add(new PrunedRange("0000-removed", "0000-removed-until", f.Clock.GetUtcNow(), acrossTheGap));
        await f.Db.SaveChangesAsync();

        var report = await f.Reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        report.IsIntact.Should().BeTrue("a declared gap is a documented step, not a cut chain");
        report.PrunedRanges.Should().ContainSingle();
    }

    [Fact]
    public async Task TheSameBreakWithoutTheDeclaration_IsReported()
    {
        // The half that makes the test above mean something: it is the declaration that rescues the
        // chain, not the mere fact that the hash differs.
        using var f = new AuditTrailFixture();
        var segments = await TwoSealedSegmentsAsync(f);

        byte[] acrossTheGap = [9, 9, 9, 9];
        var second = await f.Db.Segments.SingleAsync(s => s.SegmentId == segments[1].SegmentId);
        second.PreviousHash = acrossTheGap;
        second.SegmentHash = AuditHashing.HashSegment(acrossTheGap, second.MerkleRoot!, second.SegmentId);
        await f.Db.SaveChangesAsync();

        var report = await f.Reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        report.IsIntact.Should().BeFalse();
        report.BrokenSegmentIds.Should().Contain(second.SegmentId);
    }
}
