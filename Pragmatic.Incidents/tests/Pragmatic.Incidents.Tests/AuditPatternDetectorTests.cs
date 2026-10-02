using Pragmatic.Testing.Assertions;
using Pragmatic.Audit;
using Pragmatic.Incidents.Audit;

namespace Pragmatic.Incidents.Tests;

/// <summary>
///     Raising an incident from a pattern in the trail — and, as often, not raising one.
/// </summary>
public class AuditPatternDetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A trail that returns what the test put in it, and records what it was asked.</summary>
    private sealed class FakeReader(params AuditEntry[] entries) : IAuditTrailReader
    {
        public AuditQuery? LastQuery { get; private set; }

        public Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(new AuditPage(entries, entries.Length));
        }

        public Task<IntegrityReport> VerifyAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default)
            => throw new NotSupportedException("The detector must not need to verify anything.");
    }

    private static AuditEntry Failed(string? subjectRef, string operation = "Security.LoginFailed")
        => new()
        {
            SegmentId = "2026-07-31T12",
            OccurredAt = Now.AddMinutes(-1),
            Category = AuditCategory.Security,
            Operation = operation,
            SubjectRef = subjectRef,
            Outcome = AuditOutcome.Failed,
        };

    private static DetectionRule Rule(int threshold, bool perSubject = false)
        => new()
        {
            Operation = "Security.LoginFailed",
            Window = TimeSpan.FromMinutes(15),
            Threshold = threshold,
            PerSubject = perSubject,
            Summary = "Repeated failed sign-ins",
        };

    private static Task<IReadOnlyList<SecurityIncident>> Scan(DetectionRule rule, params AuditEntry[] entries)
        => new AuditPatternDetector(new FakeReader(entries), new FixedClock(Now)).ScanAsync([rule]);

    [Fact]
    public async Task EnoughFailuresInTheWindow_RaiseADetectedIncident()
    {
        var incidents = await Scan(Rule(threshold: 3), Failed("s1"), Failed("s2"), Failed("s3"));

        incidents.Should().ContainSingle();
        incidents[0].Stage.Should().Be(IncidentStage.Detected);
        incidents[0].DetectedAt.Should().Be(Now);
        incidents[0].Summary.Should().Contain("3 × Security.LoginFailed");
    }

    [Fact]
    public async Task BelowTheThreshold_RaisesNothing()
    {
        (await Scan(Rule(threshold: 3), Failed("s1"), Failed("s2"))).Should().BeEmpty();
    }

    [Fact]
    public async Task ARaisedIncidentIsNeverAssessed()
    {
        // The line the framework does not cross: it starts the clock, a human decides what it means.
        var incidents = await Scan(Rule(threshold: 1), Failed("s1"));

        incidents[0].AssessmentNote.Should().BeNull();
        incidents[0].IsClosed.Should().BeFalse();
        incidents[0].NextObligation.Should().NotBeNull("the clock has to be running, or nothing was raised");
    }

    [Fact]
    public async Task OtherOperationsInTheSameCategory_AreNotCounted()
    {
        // The query filters by category, so unrelated security entries do arrive and must be excluded
        // here. Without this the threshold would be reached by ordinary traffic.
        var incidents = await Scan(
            Rule(threshold: 3),
            Failed("s1"),
            Failed("s2", operation: "Security.PermissionDenied"),
            Failed("s3", operation: "Security.PermissionDenied"));

        incidents.Should().BeEmpty();
    }

    [Fact]
    public async Task PerSubject_RaisesOnePerAccountThatCrossesTheThreshold()
    {
        var incidents = await Scan(
            Rule(threshold: 2, perSubject: true),
            Failed("victim"), Failed("victim"), Failed("victim"),
            Failed("bystander"));

        incidents.Should().ContainSingle();
        incidents[0].Summary.Should().Contain("against subject victim");
    }

    [Fact]
    public async Task PerSubject_CannotSeeAttemptsAgainstAccountsThatDoNotExist()
    {
        // Not a gap to fix here — it follows from a deliberate decision elsewhere. A failed login against
        // an unknown identity carries no SubjectRef, because pseudonymising an address typed by a
        // stranger would let anyone fill the subject registry. So per-subject counting skips them.
        (await Scan(Rule(threshold: 2, perSubject: true), Failed(null), Failed(null), Failed(null)))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task TheGlobalRuleIsWhatCatchesSprayingAcrossUnknownAccounts()
    {
        // The other half of the pair above: this is why the documentation says to configure both.
        var incidents = await Scan(Rule(threshold: 3), Failed(null), Failed(null), Failed(null));

        incidents.Should().ContainSingle();
        incidents[0].Summary.Should().Contain("across the system");
    }

    [Fact]
    public async Task TheWindowIsAskedOfTheTrail_NotFilteredAfterwards()
    {
        var reader = new FakeReader(Failed("s1"));
        await new AuditPatternDetector(reader, new FixedClock(Now)).ScanAsync([Rule(threshold: 1)]);

        reader.LastQuery!.From.Should().Be(Now - TimeSpan.FromMinutes(15));
        reader.LastQuery.Until.Should().Be(Now);
        reader.LastQuery.Category.Should().Be(AuditCategory.Security);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ARuleThatCannotFailToTrigger_IsRejected(int threshold)
    {
        // A threshold of zero raises an incident every time it is scanned, for ever. Accepting it would
        // produce a stream of alerts that trains people to close them unread.
        var act = async () => await Scan(Rule(threshold), Failed("s1"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RepeatedIncidentsCarryTheSameShapeOfId_SoDuplicatesAreRecognisable()
    {
        // The detector is a pure function of the trail and the window: it has no memory, so scanning an
        // overlapping window twice raises twice. That is the caller's to deduplicate, and the id is what
        // makes it possible.
        var incidents = await Scan(Rule(threshold: 1, perSubject: true), Failed("victim"));

        incidents[0].IncidentId.Should().StartWith("audit:Security.LoginFailed:victim:");
    }
}
