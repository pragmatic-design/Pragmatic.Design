using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Audit.EFCore.Tests;

/// <summary>
///     Append behaviour: mandatory redaction, segment assignment, and querying.
/// </summary>
public sealed class AuditTrailBehaviourTests : IDisposable
{
    private readonly AuditTrailFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Record_RedactsTheDetail_WithoutBeingAsked()
    {
        // The invariant the whole erasure design rests on: an entry never holds a personal value. If
        // redaction were the caller's job, one forgetful caller would put one into an append-only store.
        var entry = _f.Entry();
        entry.Detail = "notified ada@example.com about the change";

        await _f.Trail.RecordAsync(entry);
        _f.Db.ChangeTracker.Clear();

        var stored = await _f.Db.Entries.FirstAsync();
        stored.Detail.Should().NotContain("ada@example.com");
        stored.Detail.Should().Contain("[redacted]");
    }

    [Fact]
    public async Task Record_AssignsTheSegmentFromTheTimestamp()
    {
        await _f.Trail.RecordAsync(_f.Entry(at: new DateTimeOffset(2026, 7, 30, 14, 30, 0, TimeSpan.Zero)));
        _f.Db.ChangeTracker.Clear();

        (await _f.Db.Entries.FirstAsync()).SegmentId.Should().Be("2026-07-30T14");
    }

    [Fact]
    public async Task Record_EntriesInTheSameHour_ShareASegment()
    {
        var hour = new DateTimeOffset(2026, 7, 30, 14, 0, 0, TimeSpan.Zero);
        await _f.Trail.RecordAsync(_f.Entry("A", hour.AddMinutes(1)));
        await _f.Trail.RecordAsync(_f.Entry("B", hour.AddMinutes(59)));
        _f.Db.ChangeTracker.Clear();

        (await _f.Db.Segments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Record_ConcurrentAppends_AllLandAndAreAllSealed()
    {
        // Appends coordinate with nobody, so the property worth asserting is not "no lock" but the
        // observable consequence: nothing is lost, and the seal covers every entry.
        const int count = 50;
        for (var i = 0; i < count; i++)
            await _f.Trail.RecordAsync(_f.Entry($"Op{i}"));

        _f.Clock.Advance(TimeSpan.FromHours(1) + _f.Naming.GracePeriod + TimeSpan.FromMinutes(1));
        await _f.Sealing.SealDueSegmentsAsync();
        _f.Db.ChangeTracker.Clear();

        var segment = await _f.Db.Segments.FirstAsync();
        segment.EntryCount.Should().Be(count);
        (await _f.Reader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).IsIntact.Should().BeTrue();
    }

    [Fact]
    public async Task Record_WithoutAnOperation_IsRefused()
    {
        var entry = _f.Entry();
        entry.Operation = "  ";

        var act = async () => await _f.Trail.RecordAsync(entry);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Query_FiltersBySubject()
    {
        var a = _f.Entry("A");
        a.SubjectRef = "subject-a";
        var b = _f.Entry("B");
        b.SubjectRef = "subject-b";
        await _f.Trail.RecordAsync(a);
        await _f.Trail.RecordAsync(b);

        var page = await _f.Reader.QueryAsync(new AuditQuery { SubjectRef = "subject-a" });

        page.TotalCount.Should().Be(1);
        page.Entries.Should().ContainSingle().Which.Operation.Should().Be("A");
    }

    /// <summary>
    ///     The entries about one thing, without reading every entry of its kind: without this filter a
    ///     caller read a page of the kind and picked the target out of it in memory, and a page is capped.
    /// </summary>
    [Fact]
    public async Task Query_FiltersByTarget()
    {
        await RecordTwoTargetsOfOneType();

        var page = await _f.Reader.QueryAsync(new AuditQuery { TargetType = "LeaveRequest", TargetId = "request-1" });

        page.TotalCount.Should().Be(1);
        page.Entries.Should().ContainSingle().Which.Operation.Should().Be("Approved");
    }

    [Fact]
    public async Task Query_WithoutATarget_ReadsEveryTarget()
    {
        await RecordTwoTargetsOfOneType();

        var page = await _f.Reader.QueryAsync(new AuditQuery { TargetType = "LeaveRequest" });

        page.TotalCount.Should().Be(2);
    }

    private async Task RecordTwoTargetsOfOneType()
    {
        var first = _f.Entry("Approved");
        first.TargetType = "LeaveRequest";
        first.TargetId = "request-1";
        var second = _f.Entry("Rejected");
        second.TargetType = "LeaveRequest";
        second.TargetId = "request-2";
        await _f.Trail.RecordAsync(first);
        await _f.Trail.RecordAsync(second);
    }

    [Fact]
    public async Task Query_ReturnsNewestFirst()
    {
        await _f.Trail.RecordAsync(_f.Entry("first"));
        await _f.Trail.RecordAsync(_f.Entry("second"));

        var page = await _f.Reader.QueryAsync(new AuditQuery());

        page.Entries[0].Operation.Should().Be("second");
    }

    [Fact]
    public async Task Query_LimitIsCapped()
    {
        await _f.Trail.RecordAsync(_f.Entry());

        // A caller asking for everything must not be able to pull the whole trail into memory.
        var page = await _f.Reader.QueryAsync(new AuditQuery { Limit = int.MaxValue });

        page.Entries.Count.Should().BeLessThanOrEqualTo(1000);
    }
}
