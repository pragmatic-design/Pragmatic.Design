using Pragmatic.Audit;

namespace Pragmatic.Incidents.Audit;

/// <summary>
///     A pattern in the audit trail worth raising an incident for.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately crude: a count of one operation inside a window. Anything cleverer — scoring,
///         baselines, anomaly models — is a product in its own right, and a framework that ships a weak
///         version of one invites people to trust it as though it were a strong one.
///     </para>
///     <para>
///         The thresholds are not defaulted to "sensible" values. What counts as too many failed logins
///         depends entirely on how many users a deployment has and how they sign in, and a default here
///         would be a number nobody chose being treated as a number someone chose.
///     </para>
/// </remarks>
public sealed record DetectionRule
{
    /// <summary>The <see cref="AuditEntry.Operation" /> to count, e.g. <c>Security.LoginFailed</c>.</summary>
    public required string Operation { get; init; }

    /// <summary>How far back to look.</summary>
    public required TimeSpan Window { get; init; }

    /// <summary>How many occurrences inside the window raise an incident.</summary>
    public required int Threshold { get; init; }

    /// <summary>
    ///     Count per subject pseudonym rather than across the whole trail.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The two settings catch different attacks and neither subsumes the other: per-subject finds
    ///         many attempts against <em>one</em> account (credential stuffing), global finds a few
    ///         attempts against <em>many</em> (password spraying, which per-subject counting never sees
    ///         because no single account crosses the threshold).
    ///     </para>
    ///     <para>
    ///         ⚠️ Per-subject counting cannot see attempts against accounts that do not exist. Those
    ///         entries carry no <c>SubjectRef</c> by design — pseudonymising an address typed by a
    ///         stranger would let anyone fill the subject registry — so they group under nothing and are
    ///         skipped. Enumeration of non-existent accounts is precisely a spraying pattern, and only a
    ///         global rule will catch it. Configure both.
    ///     </para>
    /// </remarks>
    public bool PerSubject { get; init; }

    /// <summary>What the raised incident says happened. The count is appended.</summary>
    public required string Summary { get; init; }
}
