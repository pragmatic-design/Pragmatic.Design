namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Thrown when an operation exceeds the configured timeout.
/// </summary>
public sealed class TimeoutRejectedException(string operationName, TimeSpan timeout)
    : Exception($"Operation '{operationName}' timed out after {timeout.TotalSeconds:F1}s")
{
    public string OperationName { get; } = operationName;
    public TimeSpan Timeout { get; } = timeout;
}
