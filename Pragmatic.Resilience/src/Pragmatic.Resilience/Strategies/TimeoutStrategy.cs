using Microsoft.Extensions.Logging;
using Pragmatic.Resilience.Diagnostics;
using Pragmatic.Resilience.Telemetry;

namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Timeout resilience strategy. Cancels the operation if it exceeds the configured timeout.
/// Supports optimistic (CancellationToken) and pessimistic (Task.Delay race) modes.
/// </summary>
public sealed class TimeoutStrategy(TimeoutOptions options, ILogger? logger = null) : IResilienceStrategy
{
    /// <summary>Order 100 — outermost strategy, wraps everything.</summary>
    public int Order => StrategyOrder.Timeout;

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct)
    {
        if (options.TimeoutType == TimeoutType.Pessimistic)
            return await ExecutePessimisticAsync(next, context, ct).ConfigureAwait(false);

        return await ExecuteOptimisticAsync(next, context, ct).ConfigureAwait(false);
    }

    private async Task<TResult> ExecuteOptimisticAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.Timeout);

        try
        {
            return await next(context, timeoutCts.Token).ConfigureAwait(false);
        }
        // Attribute the cancellation to the timeout only when OUR linked source actually fired:
        // an OCE raised by the operation's own internal token must propagate as-is, not be
        // misreported as a timeout.
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            if (logger is not null)
            {
                ResilienceLogMessages.LogTimeout(
                    logger, context.OperationName, options.Timeout.TotalMilliseconds);
            }

            ResilienceDiagnostics.Timeouts.Add(1,
                new KeyValuePair<string, object?>("operation.name", context.OperationName));

            throw new TimeoutRejectedException(context.OperationName, options.Timeout);
        }
    }

    private async Task<TResult> ExecutePessimisticAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct)
    {
        // Do NOT use 'using' here: disposing the CTS while operationTask still holds its Token
        // can cause ObjectDisposedException inside the still-running operation.
        // The CTS is cancelled (and thus effectively inert) before we abandon the task;
        // GC will collect it once operationTask completes and releases the reference.
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Dedicated source for the timeout timer so it can be cancelled (and its underlying Timer
        // released) the instant the operation wins the race — otherwise the Task.Delay stays scheduled
        // until the full timeout elapses, accumulating live timers under high throughput.
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var operationTask = next(context, timeoutCts.Token);
        var delayTask = Task.Delay(options.Timeout, delayCts.Token);

        var completedTask = await Task.WhenAny(operationTask, delayTask).ConfigureAwait(false);

        if (completedTask == delayTask)
        {
            await timeoutCts.CancelAsync().ConfigureAwait(false);

            // Observe the abandoned task to prevent UnobservedTaskException; dispose CTS after it finishes.
            _ = operationTask.ContinueWith(
                static (t, state) =>
                {
                    _ = t.Exception;
                    ((CancellationTokenSource)state!).Dispose();
                },
                timeoutCts,
                TaskContinuationOptions.ExecuteSynchronously);

            // delayTask also completes (canceled) on EXTERNAL cancellation — that is not a
            // timeout and must propagate as OperationCanceledException, not TimeoutRejected.
            ct.ThrowIfCancellationRequested();

            if (logger is not null)
            {
                ResilienceLogMessages.LogTimeout(
                    logger, context.OperationName, options.Timeout.TotalMilliseconds);
            }

            ResilienceDiagnostics.Timeouts.Add(1,
                new KeyValuePair<string, object?>("operation.name", context.OperationName));

            throw new TimeoutRejectedException(context.OperationName, options.Timeout);
        }

        // Operation completed before timeout — cancel the pending delay timer (releasing it now instead
        // of at timeout) and dispose the CTS we no longer need.
        await delayCts.CancelAsync().ConfigureAwait(false);
        timeoutCts.Dispose();
        return await operationTask.ConfigureAwait(false);
    }
}
