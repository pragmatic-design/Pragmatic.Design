using Pragmatic.Result;

namespace Pragmatic.Resilience.Errors;

/// <summary>Bulkhead capacity exceeded — too many concurrent requests.</summary>
public sealed record BulkheadRejectedError(string OperationName, int MaxConcurrency) : Error
{
    public override string Code => "BULKHEAD_REJECTED";
    public override int StatusCode => 429;
    public override string Title => $"Too many concurrent requests for '{OperationName}' (max: {MaxConcurrency})";
}
