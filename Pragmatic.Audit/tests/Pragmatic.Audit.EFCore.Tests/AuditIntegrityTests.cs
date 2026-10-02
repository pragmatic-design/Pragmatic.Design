using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Audit.EFCore.Tests;

/// <summary>
///     The setpoints the trail exists to satisfy: tampering is detected and located, retention does not
///     look like tampering, and a sealed segment refuses late writes instead of absorbing them.
/// </summary>
public sealed class AuditIntegrityTests : IDisposable
{
    private readonly AuditTrailFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Moves past the segment window and its grace period, then seals.</summary>
    private async Task SealCurrentAsync()
    {
        _f.Clock.Advance(TimeSpan.FromHours(1) + _f.Naming.GracePeriod + TimeSpan.FromMinutes(1));
        await _f.Sealing.SealDueSegmentsAsync();
    }

    [Fact]
    public async Task Sealing_ProducesAChainedSegment()
    {
        await _f.Trail.RecordAsync(_f.Entry());

        var sealed1 = await SealAndReturnAsync();

        sealed1.IsSealed.Should().BeTrue();
        sealed1.MerkleRoot.Should().NotBeNull();
        sealed1.SegmentHash.Should().NotBeNull();
        sealed1.EntryCount.Should().Be(1);
    }

    private async Task<AuditSegment> SealAndReturnAsync()
    {
        await SealCurrentAsync();
        _f.Db.ChangeTracker.Clear();
        return await _f.Db.Segments.OrderBy(s => s.SegmentId).FirstAsync();
    }

    [Fact]
    public async Task Verify_IntactTrail_Passes()
    {
        await _f.Trail.RecordAsync(_f.Entry("A"));
        await _f.Trail.RecordAsync(_f.Entry("B"));
        await SealCurrentAsync();

        var report = await _f.Reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        report.IsIntact.Should().BeTrue();
        report.SegmentsChecked.Should().Be(1);
    }

    [Fact]
    public async Task Verify_EntryAlteredAfterSealing_FailsAndNamesTheSegment()
    {
        // The setpoint: "the trail is broken" is not actionable; "this segment is broken" is.
        await _f.Trail.RecordAsync(_f.Entry("A"));
        await SealCurrentAsync();
        _f.Db.ChangeTracker.Clear();

        var segmentId = (await _f.Db.Segments.FirstAsync()).SegmentId;
        var entry = await _f.Db.Entries.FirstAsync();
        entry.Operation = "A.Tampered";
        await _f.Db.SaveChangesAsync();

        var report = await _f.Reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        report.IsIntact.Should().BeFalse();
        report.BrokenSegmentIds.Should().ContainSingle().Which.Should().Be(segmentId);
    }

    [Fact]
    public async Task Verify_EntryDeletedAfterSealing_Fails()
    {
        await _f.Trail.RecordAsync(_f.Entry("A"));
        await _f.Trail.RecordAsync(_f.Entry("B"));
        await SealCurrentAsync();
        _f.Db.ChangeTracker.Clear();

        await _f.Db.Entries.Where(e => e.Operation == "B").ExecuteDeleteAsync();

        (await _f.Reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue))
            .IsIntact.Should().BeFalse("removing an entry must be as visible as changing one");
    }

    [Fact]
    public async Task Verify_EntryInsertedIntoASealedSegment_Fails()
    {
        await _f.Trail.RecordAsync(_f.Entry("A"));
        await SealCurrentAsync();
        _f.Db.ChangeTracker.Clear();

        var segmentId = (await _f.Db.Segments.FirstAsync()).SegmentId;
        _f.Db.Entries.Add(new AuditEntry
        {
            SegmentId = segmentId,
            OccurredAt = _f.Clock.GetUtcNow(),
            Category = AuditCategory.Data,
            Operation = "Smuggled",
            Outcome = AuditOutcome.Success
        });
        await _f.Db.SaveChangesAsync();

        (await _f.Reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue))
            .IsIntact.Should().BeFalse();
    }

    [Fact]
    public async Task Record_IntoAnAlreadySealedSegment_IsRefused()
    {
        // Refused loudly rather than absorbed: an entry outside the root would surface later as a
        // false tampering alarm, at a point where nobody can explain it.
        var at = _f.Clock.GetUtcNow();
        await _f.Trail.RecordAsync(_f.Entry("A", at));
        await SealCurrentAsync();

        var act = async () => await _f.Trail.RecordAsync(_f.Entry("Late", at));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already sealed*");
    }

    /// <summary>
    ///     Several segments due at once — the first pass after the sealer was down, or after hours of
    ///     writing with nobody sealing — chain each to the one before it, as one at a time would.
    /// </summary>
    /// <remarks>
    ///     The predecessor cannot be looked up in the database, where a segment sealed earlier in the same
    ///     pass is still open until the pass saves: the second segment would link past the first, and the
    ///     next verification would report an intact trail as broken.
    /// </remarks>
    [Fact]
    public async Task Sealing_SeveralDueSegmentsInOnePass_ChainsEachToTheOneBefore()
    {
        await _f.Trail.RecordAsync(_f.Entry("A"));
        _f.Clock.Advance(TimeSpan.FromHours(1));
        await _f.Trail.RecordAsync(_f.Entry("B"));
        _f.Clock.Advance(TimeSpan.FromHours(1));
        await _f.Trail.RecordAsync(_f.Entry("C"));
        _f.Clock.Advance(TimeSpan.FromHours(1) + _f.Naming.GracePeriod + TimeSpan.FromMinutes(1));

        var sealedIds = await _f.Sealing.SealDueSegmentsAsync();
        var report = await _f.Reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        sealedIds.Should().HaveCount(3);
        report.SegmentsChecked.Should().Be(3);
        report.IsIntact.Should().BeTrue("nothing was altered: {0} reported broken",
            string.Join(", ", report.BrokenSegmentIds));
    }

    [Fact]
    public async Task Sealing_DoesNotSealASegmentStillInsideItsGracePeriod()
    {
        // Sealing early is how a legitimate in-flight write becomes a permanent false positive.
        await _f.Trail.RecordAsync(_f.Entry());

        _f.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));   // window closed, grace not elapsed
        var sealedIds = await _f.Sealing.SealDueSegmentsAsync();

        sealedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Verify_AfterPruningAnExpiredSegment_StillPasses()
    {
        // Retention must not be indistinguishable from tampering.
        await _f.Trail.RecordAsync(_f.Entry("old"));
        await SealCurrentAsync();

        await _f.Trail.RecordAsync(_f.Entry("recent"));
        await SealCurrentAsync();

        var pruned = await _f.Retention.PruneOlderThanAsync(TimeSpan.FromHours(2));

        pruned.Should().NotBeNull("the oldest segment was past retention");
        var report = await _f.Reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        report.IsIntact.Should().BeTrue();
        report.PrunedRanges.Should().ContainSingle();
    }

    [Fact]
    public async Task Prune_RemovesTheSegmentsEntriesToo()
    {
        await _f.Trail.RecordAsync(_f.Entry("old"));
        await SealCurrentAsync();
        await _f.Trail.RecordAsync(_f.Entry("recent"));
        await SealCurrentAsync();

        await _f.Retention.PruneOlderThanAsync(TimeSpan.FromHours(2));
        _f.Db.ChangeTracker.Clear();

        (await _f.Db.Entries.AnyAsync(e => e.Operation == "old")).Should().BeFalse();
        (await _f.Db.Entries.AnyAsync(e => e.Operation == "recent")).Should().BeTrue();
    }

    [Fact]
    public async Task Prune_NothingOldEnough_DoesNothing()
    {
        await _f.Trail.RecordAsync(_f.Entry());
        await SealCurrentAsync();

        (await _f.Retention.PruneOlderThanAsync(TimeSpan.FromDays(365))).Should().BeNull();
    }
}
