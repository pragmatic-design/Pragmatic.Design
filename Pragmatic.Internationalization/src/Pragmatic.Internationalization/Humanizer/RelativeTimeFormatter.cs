using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Internationalization.Humanizer;

/// <summary>
///     Formats relative time (e.g., "2 hours ago", "in 3 days") using localized strings.
/// </summary>
public sealed class RelativeTimeFormatter
{
    private readonly IStringLocalizer _localizer;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Creates a new RelativeTimeFormatter.
    /// </summary>
    /// <param name="localizer">The string localizer used for translations.</param>
    /// <param name="timeProvider">
    ///     Optional time provider for testability. Defaults to <see cref="TimeProvider.System"/>.
    /// </param>
    public RelativeTimeFormatter(IStringLocalizer localizer, TimeProvider? timeProvider = null)
    {
        ThrowIfNull(localizer);
        _localizer = localizer;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    ///     Formats a TimeSpan as relative time (e.g., "2 hours ago").
    /// </summary>
    public TranslationResult Format(TimeSpan elapsed)
    {
        var totalSeconds = Math.Abs(elapsed.TotalSeconds);

        // Just now (< 5 seconds)
        if (totalSeconds < 5)
            return _localizer[Keys.Now];

        // Seconds (< 60 seconds)
        if (totalSeconds < 60)
            return _localizer.Plural(Keys.Seconds, (int)totalSeconds);

        // Minutes (< 60 minutes)
        var totalMinutes = elapsed.TotalMinutes;
        if (Math.Abs(totalMinutes) < 60)
            return _localizer.Plural(Keys.Minutes, (int)Math.Abs(totalMinutes));

        // Hours (< 24 hours)
        var totalHours = elapsed.TotalHours;
        if (Math.Abs(totalHours) < 24)
            return _localizer.Plural(Keys.Hours, (int)Math.Abs(totalHours));

        // Yesterday (24-48 hours)
        var totalDays = elapsed.TotalDays;
        if (Math.Abs(totalDays) < 2)
            return _localizer[Keys.Yesterday];

        // Days (< 7 days)
        if (Math.Abs(totalDays) < 7)
            return _localizer.Plural(Keys.Days, (int)Math.Abs(totalDays));

        // Weeks (< 4 weeks)
        if (Math.Abs(totalDays) < 28)
        {
            var weeks = (int)(Math.Abs(totalDays) / 7);
            return _localizer.Plural(Keys.Weeks, weeks);
        }

        // Months (< 12 months)
        if (Math.Abs(totalDays) < 365)
        {
            var months = (int)(Math.Abs(totalDays) / 30);
            return _localizer.Plural(Keys.Months, Math.Max(1, months));
        }

        // Years
        var years = (int)(Math.Abs(totalDays) / 365);
        return _localizer.Plural(Keys.Years, Math.Max(1, years));
    }

    /// <summary>
    ///     Formats a DateTimeOffset as relative time from now.
    /// </summary>
    public TranslationResult Format(DateTimeOffset from)
    {
        var elapsed = _timeProvider.GetUtcNow() - from;
        return Format(elapsed);
    }

    /// <summary>
    ///     Formats a DateTimeOffset as relative time from a specific point.
    /// </summary>
    public TranslationResult Format(DateTimeOffset from, DateTimeOffset to)
    {
        var elapsed = to - from;
        return Format(elapsed);
    }

    /// <summary>
    ///     Standard translation keys used for relative time.
    ///     These should be present in your translation files.
    /// </summary>
    public static class Keys
    {
        /// <summary>Translation key for "just now" (less than 5 seconds ago).</summary>
        public const string Now = "TimeAgo.now";

        /// <summary>Translation key for seconds ago (plural-aware).</summary>
        public const string Seconds = "TimeAgo.seconds";

        /// <summary>Translation key for minutes ago (plural-aware).</summary>
        public const string Minutes = "TimeAgo.minutes";

        /// <summary>Translation key for hours ago (plural-aware).</summary>
        public const string Hours = "TimeAgo.hours";

        /// <summary>Translation key for "yesterday".</summary>
        public const string Yesterday = "TimeAgo.yesterday";

        /// <summary>Translation key for days ago (plural-aware).</summary>
        public const string Days = "TimeAgo.days";

        /// <summary>Translation key for weeks ago (plural-aware).</summary>
        public const string Weeks = "TimeAgo.weeks";

        /// <summary>Translation key for months ago (plural-aware).</summary>
        public const string Months = "TimeAgo.months";

        /// <summary>Translation key for years ago (plural-aware).</summary>
        public const string Years = "TimeAgo.years";
    }
}