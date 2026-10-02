using System.Collections.Concurrent;

namespace Pragmatic.Resilience.State;

/// <summary>
/// In-memory circuit breaker state store. Thread-safe, per-process.
/// For distributed scenarios, use a Redis/DB-backed implementation.
/// </summary>
public sealed class InMemoryCircuitBreakerStateStore(TimeProvider? timeProvider = null) : ICircuitBreakerStateStore
{
    private readonly ConcurrentDictionary<string, CircuitData> _circuits = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<CircuitSnapshot> GetSnapshotAsync(string circuitKey, CancellationToken ct = default)
    {
        var data = _circuits.GetOrAdd(circuitKey, _ => new CircuitData());

        lock (data)
        {
            var now = _timeProvider.GetUtcNow();
            return Task.FromResult(new CircuitSnapshot
            {
                State = data.State,
                FailureCount = data.FailureCount,
                SuccessCount = data.SuccessCount,
                LastFailureTime = data.LastFailureTime,
                OpenedAt = data.OpenedAt,
                BreakDuration = data.BreakDuration,
                Now = now
            });
        }
    }

    public Task RecordSuccessAsync(string circuitKey, CancellationToken ct = default)
    {
        var data = _circuits.GetOrAdd(circuitKey, _ => new CircuitData());

        lock (data)
        {
            data.SuccessCount++;

            if (data.State == CircuitState.HalfOpen)
            {
                // Probe succeeded — close the circuit
                data.State = CircuitState.Closed;
                data.FailureCount = 0;
                data.OpenedAt = null;
                data.BreakDuration = null;
            }
            else if (data.State == CircuitState.Closed)
            {
                // Consecutive-failure semantics (as documented): a success resets the failure run so
                // isolated failures spread over time never accumulate to the threshold and trip a
                // circuit that is, in aggregate, healthy.
                data.FailureCount = 0;
            }
        }

        return Task.CompletedTask;
    }

    public Task RecordFailureAsync(string circuitKey, CancellationToken ct = default)
    {
        var data = _circuits.GetOrAdd(circuitKey, _ => new CircuitData());

        lock (data)
        {
            data.FailureCount++;
            data.LastFailureTime = _timeProvider.GetUtcNow();

            if (data.State == CircuitState.HalfOpen)
            {
                // Probe failed — re-open. Reset both counts so they don't accumulate across
                // open/half-open cycles (the consecutive-failure count restarts from the next Closed run).
                data.State = CircuitState.Open;
                data.OpenedAt = _timeProvider.GetUtcNow();
                data.SuccessCount = 0;
                data.FailureCount = 0;
            }
        }

        return Task.CompletedTask;
    }

    public Task TransitionToAsync(string circuitKey, CircuitState newState, TimeSpan? breakDuration = null, CancellationToken ct = default)
    {
        var data = _circuits.GetOrAdd(circuitKey, _ => new CircuitData());

        lock (data)
        {
            data.State = newState;

            if (newState == CircuitState.Open)
            {
                data.OpenedAt = _timeProvider.GetUtcNow();
                data.BreakDuration = breakDuration;
                data.SuccessCount = 0;
            }
            else if (newState == CircuitState.HalfOpen)
            {
                data.SuccessCount = 0;
            }
            else if (newState == CircuitState.Closed)
            {
                data.FailureCount = 0;
                data.SuccessCount = 0;
                data.OpenedAt = null;
                data.BreakDuration = null;
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryTransitionToHalfOpenAsync(string circuitKey, DateTimeOffset now, CancellationToken ct = default)
    {
        var data = _circuits.GetOrAdd(circuitKey, _ => new CircuitData());

        lock (data)
        {
            if (data.State == CircuitState.Open &&
                data.OpenedAt.HasValue &&
                data.BreakDuration.HasValue &&
                now >= data.OpenedAt.Value + data.BreakDuration.Value)
            {
                data.State = CircuitState.HalfOpen;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }

    private sealed class CircuitData
    {
        public CircuitState State;
        public int FailureCount;
        public int SuccessCount;
        public DateTimeOffset? LastFailureTime;
        public DateTimeOffset? OpenedAt;
        public TimeSpan? BreakDuration;
    }
}
