namespace Pragmatic.Temporal.Types;

/// <summary>
///     Exception thrown when attempting to create a ZonedDateTime with a non-existent local time
///     and the policy is set to <see cref="NonExistentTimePolicy.ThrowException" />.
/// </summary>
public sealed class NonExistentTimeException : InvalidOperationException
{
    /// <summary>Creates a new NonExistentTimeException.</summary>
    public NonExistentTimeException(DateTime localTime, TimeZoneInfo timeZone)
        : base($"The local time '{localTime:yyyy-MM-dd HH:mm:ss}' does not exist in timezone '{timeZone.Id}'. " +
               $"This time was skipped due to DST spring forward. Specify NonExistentTimePolicy to resolve.")
    {
        LocalTime = localTime;
        TimeZone = timeZone;
    }

    /// <summary>The non-existent local time.</summary>
    public DateTime LocalTime { get; }

    /// <summary>The timezone where the time doesn't exist.</summary>
    public TimeZoneInfo TimeZone { get; }
}
