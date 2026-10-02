namespace Pragmatic.Incidents;

/// <summary>
///     A security incident and the reporting clocks running against it.
/// </summary>
/// <remarks>
///     <para>
///         The framework's job here is narrow on purpose: notice, start the clocks, and make the
///         remaining time visible. <b>It does not decide whether an incident is notifiable</b> — that is
///         a judgement about impact, and automating it would produce both kinds of error, reporting
///         noise and silence about the one that mattered.
///     </para>
///     <para>
///         What it can do, and what is easy to get wrong by hand, is arithmetic against a deadline that
///         started at a moment nobody wrote down.
///     </para>
/// </remarks>
public sealed class SecurityIncident
{
    /// <summary>Identifier of the incident.</summary>
    public required string IncidentId { get; set; }

    /// <summary>
    ///     When it was detected. Every deadline runs from here — not from when it happened, which is
    ///     usually unknowable, and not from when someone got round to recording it.
    /// </summary>
    public DateTimeOffset DetectedAt { get; set; }

    /// <summary>Short description of what was noticed.</summary>
    public required string Summary { get; set; }

    /// <summary>Where the incident stands.</summary>
    public IncidentStage Stage { get; set; } = IncidentStage.Detected;

    /// <summary>The windows that apply.</summary>
    public IncidentDeadlines Deadlines { get; set; } = IncidentDeadlines.Nis2;

    /// <summary>Why it was judged notifiable, or not. Set at assessment.</summary>
    public string? AssessmentNote { get; set; }

    /// <summary>When each obligation was met.</summary>
    public DateTimeOffset? EarlyWarningAt { get; set; }

    /// <summary>When the full notification was sent.</summary>
    public DateTimeOffset? NotificationAt { get; set; }

    /// <summary>When the final report was filed.</summary>
    public DateTimeOffset? FinalReportAt { get; set; }

    /// <summary>When the early warning is due.</summary>
    public DateTimeOffset EarlyWarningDueAt => DetectedAt + Deadlines.EarlyWarning;

    /// <summary>When the full notification is due.</summary>
    public DateTimeOffset NotificationDueAt => DetectedAt + Deadlines.Notification;

    /// <summary>When the final report is due.</summary>
    public DateTimeOffset FinalReportDueAt => DetectedAt + Deadlines.FinalReport;

    /// <summary>True once no further reporting is expected.</summary>
    public bool IsClosed => Stage is IncidentStage.FinalReportFiled or IncidentStage.ClosedNotNotifiable;

    /// <summary>
    ///     The next obligation that has not been met, and when it is due — or null when none remain.
    /// </summary>
    public (string Obligation, DateTimeOffset DueAt)? NextObligation
    {
        get
        {
            if (Stage == IncidentStage.ClosedNotNotifiable)
                return null;

            if (EarlyWarningAt is null) return ("early warning", EarlyWarningDueAt);
            if (NotificationAt is null) return ("notification", NotificationDueAt);
            if (FinalReportAt is null) return ("final report", FinalReportDueAt);

            return null;
        }
    }

    /// <summary>Whether the next obligation is already late.</summary>
    public bool IsOverdue(DateTimeOffset now)
        => NextObligation is { } next && now > next.DueAt;

    /// <summary>How long remains before the next obligation; negative once it has passed.</summary>
    public TimeSpan? TimeRemaining(DateTimeOffset now)
        => NextObligation is { } next ? next.DueAt - now : null;

    /// <summary>
    ///     Records the human judgement about whether this incident is notifiable.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     No note given. "Not notifiable" without a reason is indistinguishable from nobody having
    ///     looked, and it is the version an inspection will assume.
    /// </exception>
    public void Assess(bool notifiable, string note, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        AssessmentNote = note;
        Stage = notifiable ? IncidentStage.Assessed : IncidentStage.ClosedNotNotifiable;
        _ = now;
    }

    /// <summary>Records that an obligation has been met.</summary>
    /// <exception cref="InvalidOperationException">The incident was never assessed, or was closed.</exception>
    public void RecordReport(IncidentStage stage, DateTimeOffset at)
    {
        if (Stage == IncidentStage.Detected)
            throw new InvalidOperationException(
                $"Incident '{IncidentId}' has not been assessed. Reporting before anyone decided whether " +
                "it is notifiable would record a judgement nobody made.");

        if (Stage == IncidentStage.ClosedNotNotifiable)
            throw new InvalidOperationException(
                $"Incident '{IncidentId}' was assessed as not notifiable and closed.");

        switch (stage)
        {
            case IncidentStage.EarlyWarningSent: EarlyWarningAt = at; break;
            case IncidentStage.NotificationSent: NotificationAt = at; break;
            case IncidentStage.FinalReportFiled: FinalReportAt = at; break;
            default:
                throw new ArgumentException($"{stage} is not a reporting obligation.", nameof(stage));
        }

        Stage = stage;
    }

    /// <summary>Opens an incident, starting its clocks at <paramref name="detectedAt" />.</summary>
    public static SecurityIncident Detect(
        string incidentId, string summary, DateTimeOffset detectedAt, IncidentDeadlines? deadlines = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        return new SecurityIncident
        {
            IncidentId = incidentId,
            Summary = summary,
            DetectedAt = detectedAt,
            Deadlines = deadlines ?? IncidentDeadlines.Nis2
        };
    }
}
