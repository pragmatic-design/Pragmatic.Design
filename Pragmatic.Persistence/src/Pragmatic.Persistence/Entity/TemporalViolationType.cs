namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Type of temporal constraint violation.
/// </summary>
public enum TemporalViolationType
{
    /// <summary>Number of active relations exceeds MaxActive.</summary>
    MaxActiveExceeded,

    /// <summary>New relation overlaps with an existing one when overlap is not allowed.</summary>
    OverlapDetected
}
