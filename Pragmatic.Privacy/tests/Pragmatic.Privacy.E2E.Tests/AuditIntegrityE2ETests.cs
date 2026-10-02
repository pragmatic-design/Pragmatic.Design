using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pragmatic.Audit;

namespace Pragmatic.Privacy.E2E.Tests;

/// <summary>
///     Whether the audit trail's integrity guarantee survives a real database.
/// </summary>
/// <remarks>
///     <para>
///         The logic was proven on SQLite. This proves it where it will actually run — and the
///         distinction has already earned its keep once in this work, when a <c>DateTimeOffset</c>
///         comparison SQLite refuses to translate would have made every time filter fail in production.
///     </para>
///     <para>
///         Tampering is done through <b>raw SQL</b>, not through the API. A test that alters entries via
///         the same code that wrote them proves the code is consistent with itself; the threat is
///         somebody with a database connection, so that is who these tests impersonate.
///     </para>
/// </remarks>
public sealed class AuditIntegrityE2ETests : IAsyncLifetime
{
    private readonly ComplianceStackFixture _f = new();

    public Task InitializeAsync() => _f.InitializeAsync();
    public Task DisposeAsync() => _f.DisposeAsync();

    private bool DockerMissing => _f.ConnectionString is null;

    private async Task RecordAsync(string operation, string subjectRef = "subject-audit")
        => await _f.Trail.RecordAsync(new AuditEntry
        {
            SegmentId = string.Empty,
            OccurredAt = _f.Clock.GetUtcNow(),
            Category = AuditCategory.Security,
            Operation = operation,
            SubjectRef = subjectRef,
            Outcome = AuditOutcome.Success
        });

    private async Task SealAsync()
    {
        _f.Clock.Advance(TimeSpan.FromHours(1) + _f.Naming.GracePeriod + TimeSpan.FromMinutes(1));
        await _f.Sealing.SealDueSegmentsAsync();
    }

    private async Task ExecuteRawAsync(string sql)
    {
        var conn = new NpgsqlConnection(_f.ConnectionString);
        await using (conn.ConfigureAwait(false))
        {
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task AnIntactTrail_VerifiesOnPostgres()
    {
        if (DockerMissing) return;

        await RecordAsync("Security.LoginFailed");
        await RecordAsync("Security.AccountLocked");
        await SealAsync();

        var report = await _f.TrailReader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        report.IsIntact.Should().BeTrue();
        report.SegmentsChecked.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AnEntryAlteredThroughRawSql_IsDetectedAndItsSegmentNamed()
    {
        if (DockerMissing) return;

        // The threat is an operator with a database connection, so that is what this does.
        await RecordAsync("Security.LoginFailed");
        await SealAsync();
        _f.AuditDb.ChangeTracker.Clear();

        var segmentId = (await _f.AuditDb.Segments.OrderBy(s => s.SegmentId).FirstAsync()).SegmentId;

        await ExecuteRawAsync(
            """UPDATE "__AuditEntries" SET "Operation" = 'Security.NothingHappened' """ +
            $"""WHERE "SegmentId" = '{segmentId}'""");

        var report = await _f.TrailReader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        report.IsIntact.Should().BeFalse();
        report.BrokenSegmentIds.Should().Contain(segmentId,
            "'the trail is broken' is not actionable; naming the segment is");
    }

    [Fact]
    public async Task AnEntryDeletedThroughRawSql_IsDetected()
    {
        if (DockerMissing) return;

        // Removing the record of an action is the likeliest tampering, and the easiest to miss.
        await RecordAsync("Security.LoginFailed");
        await RecordAsync("Security.PermissionDenied");
        await SealAsync();

        await ExecuteRawAsync("""DELETE FROM "__AuditEntries" WHERE "Operation" = 'Security.PermissionDenied'""");

        (await _f.TrailReader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue))
            .IsIntact.Should().BeFalse();
    }

    [Fact]
    public async Task AnEntrySmuggledIntoASealedSegment_IsDetected()
    {
        if (DockerMissing) return;

        await RecordAsync("Security.LoginFailed");
        await SealAsync();
        _f.AuditDb.ChangeTracker.Clear();

        var segmentId = (await _f.AuditDb.Segments.OrderBy(s => s.SegmentId).FirstAsync()).SegmentId;
        var ticks = _f.Clock.GetUtcNow().UtcTicks;

        await ExecuteRawAsync(
            """INSERT INTO "__AuditEntries" ("SegmentId","OccurredAt","Category","Operation","Outcome") """ +
            $"""VALUES ('{segmentId}',{ticks},0,'Security.Forged',0)""");

        (await _f.TrailReader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue))
            .IsIntact.Should().BeFalse();
    }

    [Fact]
    public async Task RetentionPruning_LeavesTheChainVerifiable()
    {
        if (DockerMissing) return;

        // Retention must not be indistinguishable from tampering, or nobody can act on a broken chain.
        await RecordAsync("Security.Old");
        await SealAsync();
        await RecordAsync("Security.Middle");
        await SealAsync();
        await RecordAsync("Security.Recent");
        await SealAsync();

        var pruned = await _f.Retention.PruneOlderThanAsync(TimeSpan.FromHours(2));
        pruned.Should().NotBeNull("three segments an hour apart must leave something older than two hours");

        var report = await _f.TrailReader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        report.IsIntact.Should().BeTrue();
        report.PrunedRanges.Should().NotBeEmpty("the gap has to be declared, or it looks like a cut chain");
        report.SegmentsChecked.Should().BeGreaterThan(0, "pruning must not empty the trail it is bounding");
    }

    [Fact]
    public async Task ATimeFilteredQuery_WorksOnPostgres()
    {
        if (DockerMissing) return;

        // The filter that a DateTimeOffset comparison would have broken on another provider.
        var start = _f.Clock.GetUtcNow();
        await RecordAsync("Security.First");

        var page = await _f.TrailReader.QueryAsync(new AuditQuery
        {
            From = start.AddMinutes(-1),
            Until = start.AddMinutes(1)
        });

        page.Entries.Should().NotBeEmpty();
    }
}
