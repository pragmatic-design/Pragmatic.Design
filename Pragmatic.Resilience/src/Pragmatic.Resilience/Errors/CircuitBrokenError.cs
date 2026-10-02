using Pragmatic.Result;

namespace Pragmatic.Resilience.Errors;

/// <summary>Circuit is open — requests are being rejected.</summary>
public sealed record CircuitBrokenError(string CircuitKey, TimeSpan BreakDuration) : Error
{
    public override string Code => "CIRCUIT_BROKEN";
    public override int StatusCode => 503;
    public override string Title => $"Circuit '{CircuitKey}' is open. Retry after {BreakDuration.TotalSeconds:F0}s";
}
