using Pragmatic.Result;

namespace Pragmatic.Resilience.Errors;

/// <summary>Operation timed out.</summary>
public sealed record TimeoutError(string OperationName, TimeSpan Timeout) : Error
{
    public override string Code => "TIMEOUT";
    public override int StatusCode => 504;
    public override string Title => $"Operation '{OperationName}' timed out after {Timeout.TotalSeconds:F1}s";
}
