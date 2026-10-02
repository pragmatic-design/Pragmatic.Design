using Pragmatic.Testing.Assertions;

namespace Pragmatic.Incidents.Tests;

/// <summary>
///     The reporting clocks, and the line the framework does not cross: it tracks obligations, it does
///     not decide whether an incident is notifiable.
/// </summary>
public sealed class SecurityIncidentTests
{
    private static readonly DateTimeOffset Detected = new(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);

    private static SecurityIncident Detect() =>
        SecurityIncident.Detect("inc-1", "unusual authentication failures", Detected);

    [Fact]
    public void DeadlinesRunFromDetection()
    {
        // Not from when it happened — usually unknowable — and not from when somebody got round to
        // recording it, which would let a delay quietly buy more time.
        var incident = Detect();

        incident.EarlyWarningDueAt.Should().Be(Detected.AddHours(24));
        incident.NotificationDueAt.Should().Be(Detected.AddHours(72));
        incident.FinalReportDueAt.Should().Be(Detected.AddDays(30));
    }

    [Fact]
    public void TheNextObligationIsTheEarlyWarning()
    {
        var next = Detect().NextObligation;

        next.Should().NotBeNull();
        next!.Value.Obligation.Should().Be("early warning");
    }

    [Fact]
    public void TimeRemaining_CountsDownAndThenGoesNegative()
    {
        var incident = Detect();

        incident.TimeRemaining(Detected.AddHours(6)).Should().Be(TimeSpan.FromHours(18));
        incident.TimeRemaining(Detected.AddHours(30)).Should().Be(TimeSpan.FromHours(-6));
        incident.IsOverdue(Detected.AddHours(30)).Should().BeTrue();
    }

    // =========================================================================
    // Assessment — the judgement the framework refuses to make
    // =========================================================================

    [Fact]
    public void AssessingRequiresANote()
    {
        // "Not notifiable" without a reason is indistinguishable from nobody having looked, and it is
        // the version an inspection will assume.
        var act = () => Detect().Assess(notifiable: false, "  ", Detected);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssessedAsNotNotifiable_ClosesTheIncidentButKeepsTheReasoning()
    {
        // Deciding not to report is a decision, and it is the one most worth being able to show.
        var incident = Detect();

        incident.Assess(notifiable: false, "no service impact, no data exposure", Detected.AddHours(2));

        incident.Stage.Should().Be(IncidentStage.ClosedNotNotifiable);
        incident.IsClosed.Should().BeTrue();
        incident.AssessmentNote.Should().Be("no service impact, no data exposure");
        incident.NextObligation.Should().BeNull();
    }

    [Fact]
    public void ReportingBeforeAssessment_IsRefused()
    {
        // It would record a judgement nobody made.
        var act = () => Detect().RecordReport(IncidentStage.EarlyWarningSent, Detected.AddHours(1));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not been assessed*");
    }

    [Fact]
    public void ReportingOnAClosedIncident_IsRefused()
    {
        var incident = Detect();
        incident.Assess(notifiable: false, "no impact", Detected);

        var act = () => incident.RecordReport(IncidentStage.EarlyWarningSent, Detected.AddHours(1));

        act.Should().Throw<InvalidOperationException>();
    }

    // =========================================================================
    // The reporting sequence
    // =========================================================================

    [Fact]
    public void EachReportAdvancesToTheNextObligation()
    {
        var incident = Detect();
        incident.Assess(notifiable: true, "possible data exposure", Detected.AddHours(1));

        incident.NextObligation!.Value.Obligation.Should().Be("early warning");

        incident.RecordReport(IncidentStage.EarlyWarningSent, Detected.AddHours(4));
        incident.NextObligation!.Value.Obligation.Should().Be("notification");

        incident.RecordReport(IncidentStage.NotificationSent, Detected.AddHours(40));
        incident.NextObligation!.Value.Obligation.Should().Be("final report");

        incident.RecordReport(IncidentStage.FinalReportFiled, Detected.AddDays(20));
        incident.NextObligation.Should().BeNull();
        incident.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void AReportedIncidentIsNoLongerOverdueForThatObligation()
    {
        var incident = Detect();
        incident.Assess(notifiable: true, "possible exposure", Detected);
        incident.RecordReport(IncidentStage.EarlyWarningSent, Detected.AddHours(4));

        incident.IsOverdue(Detected.AddHours(30)).Should().BeFalse("the 72-hour window is still open");
        incident.IsOverdue(Detected.AddHours(80)).Should().BeTrue("but that one has now passed too");
    }

    [Fact]
    public void RecordingAStageThatIsNotAnObligation_IsRefused()
    {
        var incident = Detect();
        incident.Assess(notifiable: true, "possible exposure", Detected);

        var act = () => incident.RecordReport(IncidentStage.Detected, Detected);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DeadlinesAreConfigurable_BecauseRegimesDiffer()
    {
        // Hard-coding one regulator's numbers would be quietly wrong everywhere else.
        var incident = SecurityIncident.Detect(
            "inc-2", "summary", Detected,
            new IncidentDeadlines(TimeSpan.FromHours(1), TimeSpan.FromHours(8), TimeSpan.FromDays(7)));

        incident.EarlyWarningDueAt.Should().Be(Detected.AddHours(1));
        incident.FinalReportDueAt.Should().Be(Detected.AddDays(7));
    }

    [Fact]
    public void DetectingWithoutASummary_IsRefused()
    {
        var act = () => SecurityIncident.Detect("inc-1", "   ", Detected);

        act.Should().Throw<ArgumentException>();
    }
}
