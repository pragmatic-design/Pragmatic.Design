namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Thrown when a request is rejected because the bulkhead capacity is full.
/// </summary>
public sealed class BulkheadRejectedException(string operationName, int maxConcurrency)
    : Exception($"Bulkhead capacity ({maxConcurrency}) exceeded for '{operationName}'")
{
    /// <summary>The operation that was rejected.</summary>
    public string OperationName { get; } = operationName;

    /// <summary>The maximum concurrency allowed.</summary>
    public int MaxConcurrency { get; } = maxConcurrency;
}
