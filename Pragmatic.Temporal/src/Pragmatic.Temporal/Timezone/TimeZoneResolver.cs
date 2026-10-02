using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static Pragmatic.Ensure.Ensure;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Temporal.Timezone;

/// <summary>
///     Resolves timezone IDs across platforms, supporting both IANA and Windows timezone formats.
/// </summary>
public static class TimeZoneResolver
{
    private static readonly ActivitySource ActivitySource = new("Pragmatic.Temporal", "1.0.0");
    private static readonly bool SIsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static readonly ConcurrentDictionary<string, TimeZoneInfo> SCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Gets a <see cref="TimeZoneInfo" /> from an IANA or Windows timezone ID.
    /// </summary>
    /// <param name="timezoneId">The timezone ID (e.g., "Europe/Rome" or "Central European Standard Time").</param>
    /// <returns>The resolved TimeZoneInfo.</returns>
    /// <exception cref="TimeZoneNotFoundException">If the timezone cannot be found.</exception>
    public static TimeZoneInfo GetTimeZone(string timezoneId)
    {
        ThrowIfNullOrWhiteSpace(timezoneId);

        if (SCache.TryGetValue(timezoneId, out var cached))
            return cached;

        using var activity = ActivitySource.StartActivity("temporal.timezone.resolution");
        activity?.SetTag(TemporalTags.InputTimeZoneId, timezoneId);
        activity?.SetTag(TemporalTags.Platform, SIsWindows ? "windows" : "unix");

        // Handle special case for UTC
        if (timezoneId.Equals("UTC", StringComparison.OrdinalIgnoreCase) ||
            timezoneId.Equals("Z", StringComparison.OrdinalIgnoreCase))
        {
            activity?.SetTag(TemporalTags.ResolutionType, "utc_special_case");
            return SCache[timezoneId] = TimeZoneInfo.Utc;
        }

        // Try direct lookup first (works with both IANA on Linux and Windows IDs on Windows)
        try
        {
            var result = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            activity?.SetTag(TemporalTags.ResolutionType, "direct");
            activity?.SetTag(TemporalTags.ResolvedTimeZoneId, result.Id);
            return SCache[timezoneId] = result;
        }
        catch (TimeZoneNotFoundException)
        {
            // Continue to try conversion
        }

        // Try IANA to Windows conversion
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timezoneId, out var windowsId) && windowsId != null)
            try
            {
                var result = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                activity?.SetTag(TemporalTags.ResolutionType, "iana_to_windows");
                activity?.SetTag(TemporalTags.ResolvedTimeZoneId, result.Id);
                return SCache[timezoneId] = result;
            }
            catch (TimeZoneNotFoundException)
            {
                // Fall through
            }

        // Try Windows to IANA conversion
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timezoneId, out var ianaId) && ianaId != null)
            try
            {
                var result = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
                activity?.SetTag(TemporalTags.ResolutionType, "windows_to_iana");
                activity?.SetTag(TemporalTags.ResolvedTimeZoneId, result.Id);
                return SCache[timezoneId] = result;
            }
            catch (TimeZoneNotFoundException)
            {
                // Fall through
            }

        activity?.SetStatus(ActivityStatusCode.Error, "Timezone not found");
        activity?.SetTag(TemporalTags.ResolutionType, "failed");

        throw new TimeZoneNotFoundException($"Timezone '{timezoneId}' not found. " +
                                            $"Tried both IANA and Windows timezone formats.");
    }

    /// <summary>
    ///     Tries to get a <see cref="TimeZoneInfo" /> from an IANA or Windows timezone ID.
    /// </summary>
    /// <param name="timezoneId">The timezone ID.</param>
    /// <param name="timeZone">The resolved TimeZoneInfo, or null if not found.</param>
    /// <returns>True if the timezone was found; false otherwise.</returns>
    public static bool TryGetTimeZone(string? timezoneId, out TimeZoneInfo? timeZone)
    {
        timeZone = null;

        if (string.IsNullOrWhiteSpace(timezoneId))
            return false;

        try
        {
            timeZone = GetTimeZone(timezoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Gets the IANA timezone ID for a <see cref="TimeZoneInfo" />.
    /// </summary>
    /// <param name="timeZone">The timezone.</param>
    /// <returns>The IANA timezone ID, or the original ID if conversion fails.</returns>
    public static string GetIanaId(TimeZoneInfo timeZone)
    {
        if (timeZone == TimeZoneInfo.Utc)
            return "UTC";

        // On non-Windows, the Id is already IANA
        if (!SIsWindows)
            return timeZone.Id;

        // On Windows, try to convert
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZone.Id, out var ianaId) && ianaId != null)
            return ianaId;

        return timeZone.Id;
    }

    /// <summary>
    ///     Gets the Windows timezone ID for a <see cref="TimeZoneInfo" />.
    /// </summary>
    /// <param name="timeZone">The timezone.</param>
    /// <returns>The Windows timezone ID, or the original ID if conversion fails.</returns>
    public static string GetWindowsId(TimeZoneInfo timeZone)
    {
        if (timeZone == TimeZoneInfo.Utc)
            return "UTC";

        // On Windows, the Id is already Windows format
        if (SIsWindows)
            return timeZone.Id;

        // On non-Windows, try to convert
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZone.Id, out var windowsId) && windowsId != null)
            return windowsId;

        return timeZone.Id;
    }

    /// <summary>
    ///     Checks if a timezone ID is valid.
    /// </summary>
    public static bool IsValidTimezone(string? timezoneId)
    {
        return TryGetTimeZone(timezoneId, out _);
    }

    /// <summary>
    ///     Gets all available system timezones.
    /// </summary>
    public static IReadOnlyCollection<TimeZoneInfo> GetAllTimeZones()
    {
        return TimeZoneInfo.GetSystemTimeZones();
    }

    /// <summary>
    ///     Common timezone constants with lazy-cached resolution.
    /// </summary>
    public static class CommonZones
    {
        private static readonly Lazy<TimeZoneInfo> SRome = new(() => GetTimeZone("Europe/Rome"));
        private static readonly Lazy<TimeZoneInfo> SNewYork = new(() => GetTimeZone("America/New_York"));
        private static readonly Lazy<TimeZoneInfo> SLosAngeles = new(() => GetTimeZone("America/Los_Angeles"));
        private static readonly Lazy<TimeZoneInfo> SChicago = new(() => GetTimeZone("America/Chicago"));
        private static readonly Lazy<TimeZoneInfo> SLondon = new(() => GetTimeZone("Europe/London"));
        private static readonly Lazy<TimeZoneInfo> SParis = new(() => GetTimeZone("Europe/Paris"));
        private static readonly Lazy<TimeZoneInfo> SBerlin = new(() => GetTimeZone("Europe/Berlin"));
        private static readonly Lazy<TimeZoneInfo> STokyo = new(() => GetTimeZone("Asia/Tokyo"));
        private static readonly Lazy<TimeZoneInfo> SShanghai = new(() => GetTimeZone("Asia/Shanghai"));
        private static readonly Lazy<TimeZoneInfo> SSydney = new(() => GetTimeZone("Australia/Sydney"));

        /// <summary>UTC timezone.</summary>
        public static TimeZoneInfo Utc => TimeZoneInfo.Utc;

        /// <summary>Local server timezone.</summary>
        public static TimeZoneInfo Local => TimeZoneInfo.Local;

        /// <summary>Europe/Rome timezone.</summary>
        public static TimeZoneInfo Rome => SRome.Value;

        /// <summary>America/New_York timezone (Eastern Time).</summary>
        public static TimeZoneInfo NewYork => SNewYork.Value;

        /// <summary>America/Los_Angeles timezone (Pacific Time).</summary>
        public static TimeZoneInfo LosAngeles => SLosAngeles.Value;

        /// <summary>America/Chicago timezone (Central Time).</summary>
        public static TimeZoneInfo Chicago => SChicago.Value;

        /// <summary>Europe/London timezone.</summary>
        public static TimeZoneInfo London => SLondon.Value;

        /// <summary>Europe/Paris timezone.</summary>
        public static TimeZoneInfo Paris => SParis.Value;

        /// <summary>Europe/Berlin timezone.</summary>
        public static TimeZoneInfo Berlin => SBerlin.Value;

        /// <summary>Asia/Tokyo timezone.</summary>
        public static TimeZoneInfo Tokyo => STokyo.Value;

        /// <summary>Asia/Shanghai timezone.</summary>
        public static TimeZoneInfo Shanghai => SShanghai.Value;

        /// <summary>Australia/Sydney timezone.</summary>
        public static TimeZoneInfo Sydney => SSydney.Value;
    }
}