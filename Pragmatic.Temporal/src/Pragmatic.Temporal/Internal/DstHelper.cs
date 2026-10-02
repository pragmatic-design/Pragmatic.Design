using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Internal;

/// <summary>
///     Shared DST helper methods used by both <see cref="ZonedDateTime" /> and
///     <see cref="Context.TemporalContext" /> to avoid code duplication.
/// </summary>
internal static class DstHelper
{
    /// <summary>
    ///     Gets the DateTime of a DST transition for a given year.
    /// </summary>
    internal static DateTime GetTransitionDateTime(int year, TimeZoneInfo.TransitionTime transition)
    {
        if (transition.IsFixedDateRule)
            return new DateTime(year, transition.Month, transition.Day,
                transition.TimeOfDay.Hour, transition.TimeOfDay.Minute, transition.TimeOfDay.Second);

        // Floating rule (e.g., "second Sunday of March")
        var firstDayOfMonth = new DateTime(year, transition.Month, 1);
        var dayOfWeek = transition.DayOfWeek;
        var week = transition.Week;

        var firstOccurrence = firstDayOfMonth.AddDays((7 + (int)dayOfWeek - (int)firstDayOfMonth.DayOfWeek) % 7);

        DateTime result;
        if (week == 5) // Last occurrence
        {
            result = firstOccurrence.AddDays(21);
            while (result.Month != transition.Month)
                result = result.AddDays(-7);
        }
        else
        {
            result = firstOccurrence.AddDays((week - 1) * 7);
        }

        return result.Add(transition.TimeOfDay.TimeOfDay);
    }

    /// <summary>
    ///     Converts a local DateTime to UTC with explicit handling of DST edge cases.
    /// </summary>
    internal static DateTimeOffset LocalToUtc(
        DateTime localTime,
        TimeZoneInfo zone,
        NonExistentTimePolicy nonExistentPolicy,
        AmbiguousTimePolicy ambiguousPolicy)
    {
        var unspecifiedLocal = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);

        // Check if time doesn't exist (DST spring forward gap)
        if (zone.IsInvalidTime(unspecifiedLocal))
        {
            if (nonExistentPolicy == NonExistentTimePolicy.ThrowException)
                throw new NonExistentTimeException(unspecifiedLocal, zone);

            // ShiftForward: find the next valid time
            var rules = zone.GetAdjustmentRules();
            foreach (var rule in rules)
                if (unspecifiedLocal.Date >= rule.DateStart && unspecifiedLocal.Date <= rule.DateEnd)
                {
                    var transitionStart = GetTransitionDateTime(unspecifiedLocal.Year, rule.DaylightTransitionStart);
                    if (unspecifiedLocal >= transitionStart &&
                        unspecifiedLocal < transitionStart.Add(rule.DaylightDelta))
                    {
                        // Skip to end of gap
                        unspecifiedLocal = transitionStart.Add(rule.DaylightDelta);
                        break;
                    }
                }
        }

        // Check if time is ambiguous (DST fall back overlap)
        if (zone.IsAmbiguousTime(unspecifiedLocal))
        {
            if (ambiguousPolicy == AmbiguousTimePolicy.ThrowException)
                throw new AmbiguousTimeException(unspecifiedLocal, zone);

            var offsets = zone.GetAmbiguousTimeOffsets(unspecifiedLocal);
            var offset = ambiguousPolicy == AmbiguousTimePolicy.UseDaylightTime
                ? offsets.Max() // Use the larger offset (daylight time)
                : offsets.Min(); // Use the smaller offset (standard time)

            return new DateTimeOffset(unspecifiedLocal, offset).ToUniversalTime();
        }

        // Normal case: unambiguous time
        var normalOffset = zone.GetUtcOffset(unspecifiedLocal);
        return new DateTimeOffset(unspecifiedLocal, normalOffset).ToUniversalTime();
    }
}
