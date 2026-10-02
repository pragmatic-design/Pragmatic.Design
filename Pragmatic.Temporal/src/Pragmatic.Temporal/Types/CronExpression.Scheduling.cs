namespace Pragmatic.Temporal.Types;

public sealed partial class CronExpression
{
    #region Scheduling

    /// <summary>
    ///     Gets the next occurrence after the specified time.
    /// </summary>
    /// <param name="from">Start time.</param>
    /// <param name="zone">Optional timezone for evaluation (for DST handling). Defaults to UTC.</param>
    /// <returns>Next occurrence, or null if no valid occurrence (e.g., DST gap).</returns>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset from, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Utc;
        var fromUtc = from.ToUniversalTime();

        // DST fall-back second pass: the wall-forward search below can only move
        // forward in wall time, so occurrences whose wall time lies at or behind the
        // starting wall time but whose standard-offset instant is still ahead of
        // 'from' would be skipped. Interval expressions (Vixie rule, see
        // IsIntervalExpression) fire in both passes of the repeated hour, so those
        // candidates must be considered too.
        var secondPass = IsIntervalExpression ? FindSecondPassCandidate(fromUtc, zone) : null;
        var wallForward = FindNextWallForward(fromUtc, zone);

        if (secondPass.HasValue && (!wallForward.HasValue || secondPass.Value < wallForward.Value))
            return secondPass;
        return wallForward;
    }

    private DateTimeOffset? FindNextWallForward(DateTimeOffset fromUtc, TimeZoneInfo zone)
    {
        // Convert to local time in the specified zone
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(fromUtc.UtcDateTime, zone);

        // Start from the next second/minute
        localTime = HasSeconds
            ? localTime.AddSeconds(1)
            : localTime.AddMinutes(1).AddSeconds(-localTime.Second).AddMilliseconds(-localTime.Millisecond);

        // Reset sub-second precision
        localTime = new DateTime(localTime.Year, localTime.Month, localTime.Day,
            localTime.Hour, localTime.Minute, HasSeconds ? localTime.Second : 0);

        // Search for next matching time (max 4 years to avoid infinite loop)
        var maxIterations = 366 * 24 * 60 * 4; // ~4 years of minutes
        for (var i = 0; i < maxIterations; i++)
        {
            if (!_month.Contains(localTime.Month))
            {
                localTime = NextMonth(localTime);
                continue;
            }

            if (!MatchesDay(localTime))
            {
                localTime = localTime.AddDays(1).Date;
                continue;
            }

            if (!_hours.Contains(localTime.Hour))
            {
                localTime = NextHour(localTime);
                continue;
            }

            if (!_minutes.Contains(localTime.Minute))
            {
                localTime = NextMinute(localTime);
                continue;
            }

            if (HasSeconds && !_seconds.Contains(localTime.Second))
            {
                localTime = localTime.AddSeconds(1);
                continue;
            }

            // Check if this time is valid (not in DST gap)
            if (zone.IsInvalidTime(localTime))
            {
                localTime = localTime.AddMinutes(1);
                continue;
            }

            // Convert back to UTC. For ambiguous (DST fall-back) local times the same
            // wall-clock maps to two instants.
            if (zone.IsAmbiguousTime(localTime))
            {
                if (IsIntervalExpression)
                {
                    // Interval schedules fire in both passes: pick the offset that
                    // yields the earliest instant strictly after 'from' (also prevents
                    // the current=next loop in GetOccurrences from stalling).
                    DateTimeOffset? best = null;
                    foreach (var ambiguousOffset in zone.GetAmbiguousTimeOffsets(localTime))
                    {
                        var candidate = new DateTimeOffset(localTime, ambiguousOffset).ToUniversalTime();
                        if (candidate > fromUtc && (best is null || candidate < best.Value))
                            best = candidate;
                    }

                    if (best.HasValue)
                        return best.Value;
                }
                else
                {
                    // Fixed-time schedules fire once, in the first (daylight = larger
                    // offset) pass, like Vixie cron and Cronos. If that instant is
                    // already at/before 'from' the job has run for this wall time.
                    var daylight = MaxOffset(zone.GetAmbiguousTimeOffsets(localTime));
                    var candidate = new DateTimeOffset(localTime, daylight).ToUniversalTime();
                    if (candidate > fromUtc)
                        return candidate;
                }

                // No usable instant at this wall time — advance past it.
                localTime = localTime.AddMinutes(1);
                continue;
            }

            var offset = zone.GetUtcOffset(localTime);
            return new DateTimeOffset(localTime, offset).ToUniversalTime();
        }

        return null;
    }

    /// <summary>
    ///     Finds the earliest occurrence in the second (standard-offset) pass of the
    ///     DST fall-back window containing 'from', if any. Only meaningful for
    ///     interval expressions; returns null when 'from' is not inside an ambiguous
    ///     window.
    /// </summary>
    private DateTimeOffset? FindSecondPassCandidate(DateTimeOffset fromUtc, TimeZoneInfo zone)
    {
        var fromWall = TimeZoneInfo.ConvertTimeFromUtc(fromUtc.UtcDateTime, zone);
        if (!zone.IsAmbiguousTime(fromWall))
            return null;

        // Walk back to the start of the ambiguous window (DST deltas are ≤ 2h; the
        // guard is generous slack, not a semantic bound).
        var windowStart = new DateTime(fromWall.Year, fromWall.Month, fromWall.Day,
            fromWall.Hour, fromWall.Minute, 0);
        var backGuard = 0;
        while (backGuard++ < 180 && zone.IsAmbiguousTime(windowStart.AddMinutes(-1)))
            windowStart = windowStart.AddMinutes(-1);

        var standard = MinOffset(zone.GetAmbiguousTimeOffsets(fromWall));

        var step = HasSeconds ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
        var wall = windowStart;
        var forwardGuard = 0;
        while (forwardGuard++ < 7300 && zone.IsAmbiguousTime(wall))
        {
            if (MatchesFields(wall))
            {
                var candidate = new DateTimeOffset(wall, standard).ToUniversalTime();
                if (candidate > fromUtc)
                    return candidate;
            }

            wall = wall.Add(step);
        }

        return null;
    }

    private static TimeSpan MaxOffset(TimeSpan[] offsets)
    {
        var max = offsets[0];
        for (var i = 1; i < offsets.Length; i++)
            if (offsets[i] > max)
                max = offsets[i];
        return max;
    }

    private static TimeSpan MinOffset(TimeSpan[] offsets)
    {
        var min = offsets[0];
        for (var i = 1; i < offsets.Length; i++)
            if (offsets[i] < min)
                min = offsets[i];
        return min;
    }

    /// <summary>
    ///     Gets multiple upcoming occurrences.
    /// </summary>
    /// <param name="from">Start time (exclusive).</param>
    /// <param name="until">Optional end time (inclusive). If null, runs until maxOccurrences is reached.</param>
    /// <param name="zone">Optional timezone for evaluation. Defaults to UTC.</param>
    /// <param name="maxOccurrences">Maximum number of occurrences to return. Defaults to 1000.</param>
    public IEnumerable<DateTimeOffset> GetOccurrences(
        DateTimeOffset from,
        DateTimeOffset? until = null,
        TimeZoneInfo? zone = null,
        int maxOccurrences = 1000)
    {
        var current = from;
        var count = 0;

        while (count < maxOccurrences)
        {
            var next = GetNextOccurrence(current, zone);
            if (!next.HasValue)
                yield break;
            if (until.HasValue && next.Value > until.Value)
                yield break;

            yield return next.Value;
            current = next.Value;
            count++;
        }
    }

    /// <summary>
    ///     Checks if the cron expression matches a specific time.
    /// </summary>
    public bool Matches(DateTimeOffset time, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Utc;
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(time.UtcDateTime, zone);

        return MatchesFields(localTime);
    }

    /// <summary>Checks every cron field against a wall-clock time.</summary>
    private bool MatchesFields(DateTime localTime)
    {
        return _month.Contains(localTime.Month)
               && MatchesDay(localTime)
               && _hours.Contains(localTime.Hour)
               && _minutes.Contains(localTime.Minute)
               && (!HasSeconds || _seconds.Contains(localTime.Second));
    }

    /// <summary>
    ///     Checks if day-of-month and day-of-week match according to the configured semantics.
    /// </summary>
    private bool MatchesDay(DateTime localTime)
    {
        var domMatches = _dayOfMonth.Matches(localTime.Day, localTime);
        var dowMatches = _dayOfWeek.MatchesDayOfWeek((int)localTime.DayOfWeek, localTime);

        if (Semantics == CronSemantics.Quartz)
            // Quartz: AND - both must match
            return domMatches && dowMatches;

        // Unix: OR with special handling for wildcards
        // If one field is *, only the other is evaluated
        if (_dayOfMonth.IsWildcard && _dayOfWeek.IsWildcard)
            return true; // Both wildcard = any day

        if (_dayOfMonth.IsWildcard)
            return dowMatches; // Only check day-of-week

        if (_dayOfWeek.IsWildcard)
            return domMatches; // Only check day-of-month

        // Neither is wildcard: OR logic
        return domMatches || dowMatches;
    }

    private static DateTime NextMonth(DateTime dt)
    {
        return new DateTime(dt.Month == 12 ? dt.Year + 1 : dt.Year, dt.Month == 12 ? 1 : dt.Month + 1, 1);
    }

    private static DateTime NextHour(DateTime dt)
    {
        return dt.AddHours(1).AddMinutes(-dt.Minute).AddSeconds(-dt.Second);
    }

    private static DateTime NextMinute(DateTime dt)
    {
        return dt.AddMinutes(1).AddSeconds(-dt.Second);
    }

    #endregion
}
