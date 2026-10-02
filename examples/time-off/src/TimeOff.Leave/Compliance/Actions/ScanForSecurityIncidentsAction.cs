using Pragmatic.Audit;
using Pragmatic.Incidents.Audit;

namespace TimeOff.Leave.Compliance.Actions;

/// <summary>
///     Looks in the audit trail for the sign-in patterns this application treats as worth an incident,
///     and answers with the reporting clock already running on each.
/// </summary>
/// <remarks>
///     <para>
///         Two rules, and neither subsumes the other. Per-subject catches many attempts against
///         <b>one</b> account — credential stuffing. Global catches a few attempts against <b>many</b>,
///         which per-subject counting never sees because no single account crosses the threshold, and
///         which is also the only way to notice attempts against accounts that do not exist: those
///         entries carry no subject by design, because pseudonymising an address a stranger typed would
///         let anyone fill the subject registry.
///     </para>
///     <para>
///         ⚠️ The numbers are this deployment's and not the framework's. What counts as too many failed
///         sign-ins depends entirely on how many people use the application and how they sign in, and a
///         default here would be a number nobody chose being read as a number somebody chose.
///     </para>
///     <para>
///         ⚠️ <b>The scan holds no memory of what it raised before</b>, so two calls over overlapping
///         windows answer with the same incident twice. That is deliberate in the detector — a
///         watermark hidden inside it would make its output depend on how often it happened to be
///         called — and it is why this is a <em>read</em>: it reports what the trail currently shows,
///         and nothing here persists an incident or decides it is notifiable.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(LeavePermissions.SecurityIncidents.Scan)]
[Endpoint(HttpVerb.Get, "api/compliance/security-incidents")]
public partial class ScanForSecurityIncidentsAction : DomainAction<IReadOnlyList<SecurityIncidentDto>>
{
    private IAuditTrailReader _trail = null!;
    private TimeProvider _clock = null!;

    /// <summary>How far back to look. Twelve hours when absent.</summary>
    public TimeSpan? Window { get; init; }

    /// <summary>How many failed sign-ins inside the window are worth an incident. Five when absent.</summary>
    public int? Threshold { get; init; }

    public override async Task<Result<IReadOnlyList<SecurityIncidentDto>, IError>> Execute(
        CancellationToken ct = default)
    {
        var window = Window ?? TimeSpan.FromHours(12);
        var threshold = Threshold ?? 5;

        // Built here rather than injected, because the detector says what it is: "a pure function of
        // the trail and the window", holding no state and no memory of what it raised before. A type
        // like that has nothing to gain from a lifetime, and registering it would invite a reader to
        // assume it remembers something between calls.
        var detector = new AuditPatternDetector(_trail, _clock);

        var incidents = await detector.ScanAsync(
            [
                new DetectionRule
                {
                    Operation = "Security.LoginFailed",
                    Window = window,
                    Threshold = threshold,
                    PerSubject = true,
                    Summary = "Repeated failed sign-ins against one account"
                },
                new DetectionRule
                {
                    Operation = "Security.LoginFailed",
                    Window = window,
                    Threshold = threshold,
                    Summary = "Failed sign-ins across the application"
                }
            ],
            ct).ConfigureAwait(false);

        var now = _clock.GetUtcNow();

        return incidents.Select(incident => SecurityIncidentDto.From(incident, now)).ToList();
    }
}
