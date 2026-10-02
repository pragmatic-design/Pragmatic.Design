using Pragmatic.Result;

namespace Pragmatic.Resilience.Errors;

/// <summary>All hedging attempts failed without a successful result.</summary>
public sealed record HedgingExhaustedError(int MaxAttempts) : Error
{
    public override string Code => "HEDGING_EXHAUSTED";
    public override int StatusCode => 503;
    public override string Title => $"All {MaxAttempts} hedging attempts failed";
}
