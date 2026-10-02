namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Thrown when all retry attempts are exhausted.
/// Wraps the last exception encountered during retries.
/// </summary>
public sealed class RetryExhaustedException(string operationName, int attempts, Exception? lastException)
    : Exception($"All {attempts} retry attempts exhausted for '{operationName}'", lastException)
{
    public string OperationName { get; } = operationName;
    public int Attempts { get; } = attempts;
}
