using Pragmatic.Audit;

namespace Pragmatic.Incidents.Audit;

/// <summary>
///     Reads the audit trail and raises <see cref="SecurityIncident" /> records for configured patterns.
/// </summary>
/// <remarks>
///     <para>
///         Everything it raises is <see cref="IncidentStage.Detected" /> and nothing else. The clock
///         starts, a human assesses. That boundary is the same one <see cref="SecurityIncident" /> draws,
///         and moving it here — "obviously not notifiable, close it" — would put the judgement back
///         inside the framework through the side door.
///     </para>
///     <para>
///         <b>This is a pure function of the trail and the window.</b> It holds no state and no memory of
///         what it raised before, so scanning an overlapping window twice raises the same incident twice.
///         Deduplication belongs to whatever persists incidents, which is the only thing that knows what
///         is already open — hiding a watermark in here would make the detector's output depend on how
///         often it happened to be called.
///     </para>
/// </remarks>
public sealed class AuditPatternDetector(IAuditTrailReader reader, TimeProvider timeProvider)
{
    /// <summary>
    ///     Upper bound on entries fetched per rule. A window wide enough to matter can hold far more
    ///     entries than anyone wants in memory, and the count only has to reach the threshold.
    /// </summary>
    private const int MaxEntriesPerRule = 1000;

    /// <summary>
    ///     Applies each rule to the trail and returns the incidents raised, newest window first.
    /// </summary>
    /// <param name="rules">Patterns to look for.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<IReadOnlyList<SecurityIncident>> ScanAsync(
        IReadOnlyList<DetectionRule> rules, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var now = timeProvider.GetUtcNow();
        var raised = new List<SecurityIncident>();

        foreach (var rule in rules)
        {
            if (rule.Threshold <= 0 || rule.Window <= TimeSpan.Zero)
                throw new ArgumentException(
                    $"Rule for '{rule.Operation}' has a threshold of {rule.Threshold} and a window of " +
                    $"{rule.Window}. A rule that cannot fail to trigger is not a detection.", nameof(rules));

            var from = now - rule.Window;

            var page = await reader.QueryAsync(
                new AuditQuery
                {
                    From = from,
                    Until = now,
                    Category = AuditCategory.Security,
                    Limit = MaxEntriesPerRule,
                },
                ct).ConfigureAwait(false);

            var matching = page.Entries.Where(e => e.Operation == rule.Operation).ToList();

            if (rule.PerSubject)
                raised.AddRange(PerSubject(rule, matching, now));
            else if (matching.Count >= rule.Threshold)
                raised.Add(Raise(rule, now, matching.Count, subjectRef: null));
        }

        return raised;
    }

    private static IEnumerable<SecurityIncident> PerSubject(
        DetectionRule rule, List<AuditEntry> matching, DateTimeOffset now)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in matching)
        {
            // No subject means the attempt was against something nobody recognised. It cannot be
            // attributed to an account, and inventing a group for it would merge unrelated attempts into
            // one phantom victim. The global form of the rule is what covers these.
            if (entry.SubjectRef is not { Length: > 0 } subject)
                continue;

            counts[subject] = counts.TryGetValue(subject, out var n) ? n + 1 : 1;
        }

        foreach (var (subject, count) in counts)
            if (count >= rule.Threshold)
                yield return Raise(rule, now, count, subject);
    }

    private static SecurityIncident Raise(
        DetectionRule rule, DateTimeOffset now, int count, string? subjectRef)
    {
        var scope = subjectRef is null ? "across the system" : $"against subject {subjectRef}";

        return new SecurityIncident
        {
            // Derived from what triggered it, not random: a caller that persists incidents can recognise
            // a re-raise of the same pattern instead of accumulating duplicates it cannot tell apart.
            IncidentId = $"audit:{rule.Operation}:{subjectRef ?? "*"}:{now.UtcTicks}",
            DetectedAt = now,
            Summary = $"{rule.Summary} — {count} × {rule.Operation} in {rule.Window} {scope}.",
            Stage = IncidentStage.Detected,
        };
    }
}
