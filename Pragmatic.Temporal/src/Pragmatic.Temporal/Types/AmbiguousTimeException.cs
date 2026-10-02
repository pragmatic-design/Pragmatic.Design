namespace Pragmatic.Temporal.Types;

/// <summary>
///     Exception thrown when attempting to create a ZonedDateTime with an ambiguous local time
///     and the policy is set to <see cref="AmbiguousTimePolicy.ThrowException" />.
/// </summary>
public sealed class AmbiguousTimeException : InvalidOperationException
{
    /// <summary>Creates a new AmbiguousTimeException.</summary>
    public AmbiguousTimeException(DateTime localTime, TimeZoneInfo timeZone)
        : base($"The local time '{localTime:yyyy-MM-dd HH:mm:ss}' is ambiguous in timezone '{timeZone.Id}'. " +
               $"This time occurs twice due to DST fall back. Specify AmbiguousTimePolicy to resolve.")
    {
        LocalTime = localTime;
        TimeZone = timeZone;
    }

    /// <summary>The ambiguous local time.</summary>
    public DateTime LocalTime { get; }

    /// <summary>The timezone where the ambiguity occurs.</summary>
    public TimeZoneInfo TimeZone { get; }
}
