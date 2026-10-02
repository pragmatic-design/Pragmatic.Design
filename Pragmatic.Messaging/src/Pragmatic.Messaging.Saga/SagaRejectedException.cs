namespace Pragmatic.Messaging.Saga;

/// <summary>
///     Thrown by a saga step to reject the current message on a <b>business</b> ground (e.g. an
///     identity that failed verification), as opposed to a transient/unexpected fault.
/// </summary>
/// <remarks>
///     The generated orchestrator treats this specially: it runs the step's compensation chain,
///     marks the instance <see cref="SagaStatus.Compensated"/>, persists it, and does <b>not</b>
///     rethrow — a business rejection is terminal, so the message is not retried or dead-lettered.
///     Any other exception is treated as a transient fault (compensate, mark <c>Faulted</c>, rethrow
///     so the delivery pipeline retries/dead-letters).
/// </remarks>
public sealed class SagaRejectedException : Exception
{
    /// <summary>Creates a rejection with a human-readable business reason.</summary>
    public SagaRejectedException(string reason) : base(reason)
    {
    }

    /// <summary>Creates a rejection with a reason and an underlying cause.</summary>
    public SagaRejectedException(string reason, Exception innerException) : base(reason, innerException)
    {
    }
}
