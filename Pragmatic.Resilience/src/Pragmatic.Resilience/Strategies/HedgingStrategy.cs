using Microsoft.Extensions.Logging;
using Pragmatic.Resilience.Diagnostics;
using Pragmatic.Resilience.Telemetry;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Hedging strategy: launches parallel attempts with a delay between them,
/// returns the first successful result. Useful for latency-sensitive operations.
/// </summary>
public sealed class HedgingStrategy : IResilienceStrategy
{
    private readonly HedgingOptions _options;
    private readonly ILogger? _logger;

    public HedgingStrategy(HedgingOptions options, ILogger? logger = null)
    {
        ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxAttempts, 1);
        _options = options;
        _logger = logger;
    }

    /// <summary>Order 150 — runs before bulkhead, wraps the entire pipeline with parallel attempts.</summary>
    public int Order => StrategyOrder.Hedging;

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct)
    {
        if (_options.MaxAttempts <= 1)
            return await next(context, ct).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedToken = cts.Token;
        var tasks = new List<Task<TResult>>(_options.MaxAttempts);

        // Last underlying failure, surfaced as the inner exception of HedgingExhaustedException so
        // outer strategies (and callers) can see what actually failed, not just the wrapper.
        Exception? lastFailure = null;

        // Launch first attempt immediately
        tasks.Add(RunAttempt(next, context.CloneForAttempt(), linkedToken));

        for (var i = 1; i < _options.MaxAttempts; i++)
        {
            // Wait for delay or first completion
            var delayTask = Task.Delay(_options.Delay, linkedToken);
            var anyCompleted = await WaitForCompletionOrDelay(tasks, delayTask).ConfigureAwait(false);

            if (anyCompleted is Task<TResult> completedAttempt)
            {
                if (completedAttempt.IsCompletedSuccessfully)
                {
                    await cts.CancelAsync().ConfigureAwait(false);

                    // Observe all losing tasks to prevent UnobservedTaskException on finalizer thread.
                    foreach (var losingTask in tasks)
                    {
                        if (!ReferenceEquals(losingTask, completedAttempt))
                            _ = losingTask.ContinueWith(static t => { _ = t.Exception; },
                                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                    }

                    ResilienceDiagnostics.HedgingSuccesses.Add(1,
                        new KeyValuePair<string, object?>("operation.name", context.OperationName));

                    if (_logger is not null)
                        ResilienceLogMessages.LogHedgingSuccess(_logger, context.OperationName, tasks.IndexOf(completedAttempt) + 1);

                    return completedAttempt.Result;
                }

                // Failed/cancelled attempt: observe it and REMOVE it from the racing set, otherwise
                // every subsequent WhenAny returns this already-completed task instantly and all the
                // remaining hedging delays are skipped (attempts fired as an immediate burst).
                // A failure still triggers the next hedged attempt right away (fall through below).
                lastFailure = completedAttempt.Exception?.InnerException ?? completedAttempt.Exception;
                tasks.Remove(completedAttempt);
            }

            // Launch next hedged attempt
            ResilienceDiagnostics.HedgingAttempts.Add(1,
                new KeyValuePair<string, object?>("operation.name", context.OperationName));

            if (_logger is not null)
                ResilienceLogMessages.LogHedgingAttempt(_logger, context.OperationName, i + 1, _options.MaxAttempts);

            tasks.Add(RunAttempt(next, context.CloneForAttempt(), linkedToken));
        }

        // All attempts launched — wait for first success
        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks).ConfigureAwait(false);

            if (completed.IsCompletedSuccessfully)
            {
                await cts.CancelAsync().ConfigureAwait(false);

                // Observe all remaining losing tasks to prevent UnobservedTaskException on finalizer thread.
                foreach (var losingTask in tasks)
                {
                    if (!ReferenceEquals(losingTask, completed))
                        _ = losingTask.ContinueWith(static t => { _ = t.Exception; },
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                }

                ResilienceDiagnostics.HedgingSuccesses.Add(1,
                    new KeyValuePair<string, object?>("operation.name", context.OperationName));

                return completed.Result;
            }

            lastFailure = completed.Exception?.InnerException ?? completed.Exception;
            tasks.Remove(completed);
        }

        // If the original cancellation token was triggered, propagate cancellation
        ct.ThrowIfCancellationRequested();

        // All failed
        throw new HedgingExhaustedException(_options.MaxAttempts, lastFailure);
    }

    private static async Task<TResult> RunAttempt<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct)
    {
        return await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the first completed task (success or failure), or null if the delay completes first.
    /// </summary>
    private static async Task<Task?> WaitForCompletionOrDelay<TResult>(
        List<Task<TResult>> tasks,
        Task delayTask)
    {
        var allTasks = new List<Task>(tasks.Count + 1);
        allTasks.AddRange(tasks);
        allTasks.Add(delayTask);

        var completed = await Task.WhenAny(allTasks).ConfigureAwait(false);

        if (completed == delayTask)
            return null;

        return completed;
    }
}
