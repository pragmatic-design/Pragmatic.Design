using Pragmatic.Result;

namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Error returned when a temporal relation constraint is violated.
/// </summary>
public sealed record TemporalOverlapError : Error
{
    public override string Code => "TEMPORAL_OVERLAP";
    public override int StatusCode => 409;
    public override string Title => "Temporal constraint violation";

    /// <summary>
    ///     The type of temporal violation that occurred.
    /// </summary>
    public required TemporalViolationType ViolationType { get; init; }

    /// <summary>
    ///     The maximum allowed active relations (if applicable).
    /// </summary>
    public int? MaxActive { get; init; }

    /// <summary>
    ///     The current count of active relations (if applicable).
    /// </summary>
    public int? CurrentActive { get; init; }
}
