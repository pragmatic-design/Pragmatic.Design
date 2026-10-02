using Microsoft.Extensions.Logging;
using Pragmatic.Resilience.Diagnostics;
using Pragmatic.Resilience.State;
using Pragmatic.Resilience.Telemetry;

namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Circuit breaker strategy. Opens after consecutive failures, rejects requests
/// while open, allows a probe after break duration elapses.
/// </summary>
public sealed class CircuitBreakerStrategy(
    CircuitBreakerOptions options,
    ICircuitBreakerStateStore stateStore,
    ILogger? logger = null)
    : IResilienceStrategy
{
    /// <summary>Order 300 — between bulkhead and retry.</summary>
    public int Order => StrategyOrder.CircuitBreaker;

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct)
    {
        var circuitKey = context.OperationKey ?? context.OperationName;
        var snapshot = await stateStore.GetSnapshotAsync(circuitKey, ct).ConfigureAwait(false);

        var isProbe = false;
        if (snapshot.State == CircuitState.Open)
        {
            // Use DateTimeOffset.UtcNow (fresh) rather than snapshot.Now (stale) to avoid
            // early half-open transitions under high contention.
            if (!await stateStore.TryTransitionToHalfOpenAsync(circuitKey, DateTimeOffset.UtcNow, ct).ConfigureAwait(false))
                throw Rejected(circuitKey, snapshot);

            // Only ONE thread wins the Open→HalfOpen transition — it becomes the probe.
            isProbe = true;
        }
        else if (snapshot.State == CircuitState.HalfOpen)
        {
            // Half-open admits exactly one request (the probe). Everyone else is rejected,
            // otherwise the full traffic would hit a dependency that is still recovering.
            throw Rejected(circuitKey, snapshot);
        }

        try
        {
            var result = await next(context, ct).ConfigureAwait(false);
            await stateStore.RecordSuccessAsync(circuitKey, ct).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // External cancellation is not a verdict on dependency health — but an abandoned
            // probe would leave the circuit HalfOpen, which now rejects every request forever.
            // Re-arm to Open so the break-duration clock restarts and a new probe follows.
            if (isProbe)
                await stateStore.TransitionToAsync(circuitKey, CircuitState.Open, options.BreakDuration, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            if (options.ShouldHandle is not null && !options.ShouldHandle(ex))
            {
                // Not a circuit-relevant failure, but the probe is inconclusive — re-arm to Open
                // rather than leaving the circuit stuck HalfOpen with no probe in flight.
                if (isProbe)
                    await stateStore.TransitionToAsync(circuitKey, CircuitState.Open, options.BreakDuration, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            await stateStore.RecordFailureAsync(circuitKey, ct).ConfigureAwait(false);

            // Check if we should trip the circuit.
            // Two GetSnapshot calls are not atomic; if another thread already opened the circuit
            // between RecordFailure and here, State will be Open and the condition below is false —
            // so the redundant TransitionToAsync is safely avoided.
            var currentSnapshot = await stateStore.GetSnapshotAsync(circuitKey, ct).ConfigureAwait(false);

            if (currentSnapshot.State == CircuitState.Closed &&
                currentSnapshot.FailureCount >= options.FailureThreshold)
            {
                await stateStore.TransitionToAsync(
                    circuitKey, CircuitState.Open, options.BreakDuration, ct).ConfigureAwait(false);

                if (logger is not null)
                    ResilienceLogMessages.LogCircuitOpened(logger, circuitKey, options.FailureThreshold);
            }

            throw;
        }
    }

    private CircuitBrokenException Rejected(string circuitKey, CircuitSnapshot snapshot)
    {
        ResilienceDiagnostics.CircuitRejections.Add(1,
            new KeyValuePair<string, object?>("circuit.key", circuitKey));

        if (logger is not null)
            ResilienceLogMessages.LogCircuitRejected(logger, circuitKey);

        return new CircuitBrokenException(circuitKey, snapshot.BreakDuration ?? options.BreakDuration);
    }
}
