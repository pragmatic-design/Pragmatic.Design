namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for time zone resolution.
/// </summary>
public static class TemporalTags
{
    /// <summary>The time zone attributed to the client.</summary>
    public const string ClientTimeZone = "pragmatic.temporal.client_timezone";

    /// <summary>The configured business time zone.</summary>
    public const string BusinessTimeZone = "pragmatic.temporal.business_timezone";

    /// <summary>How the client time zone was detected.</summary>
    public const string DetectionStrategy = "pragmatic.temporal.detection_strategy";

    /// <summary>The time zone id as supplied.</summary>
    public const string InputTimeZoneId = "pragmatic.temporal.input_timezone_id";

    /// <summary>The time zone id after resolution.</summary>
    public const string ResolvedTimeZoneId = "pragmatic.temporal.resolved_timezone_id";

    /// <summary>How the id was resolved (e.g. IANA, Windows).</summary>
    public const string ResolutionType = "pragmatic.temporal.resolution_type";

    /// <summary>The platform whose time zone database was used.</summary>
    public const string Platform = "pragmatic.temporal.platform";
}
