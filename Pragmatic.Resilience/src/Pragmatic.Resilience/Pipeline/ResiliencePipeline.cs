using System.Diagnostics;
using Pragmatic.Resilience.Diagnostics;
using Pragmatic.Telemetry;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Resilience.Pipeline;

/// <summary>
/// Composes multiple <see cref="IResilienceStrategy"/> instances into a single pipeline.
/// Strategies are executed in order (lowest Order first = most external wrapper).
/// </summary>
public sealed class ResiliencePipeline : IResiliencePipeline, IDisposable, IAsyncDisposable
{
    private readonly IResilienceStrategy[] _strategies;

    public ResiliencePipeline(IEnumerable<IResilienceStrategy> strategies)
    {
        _strategies = strategies.OrderBy(s => s.Order).ToArray();
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> operation,
        ResilienceContext context,
        CancellationToken ct = default)
    {
        if (_strategies.Length == 0)
            return await operation(context, ct).ConfigureAwait(false);

        var policyName = context.OperationKey ?? "unknown";
        using var activity = ResilienceDiagnostics.ActivitySource.StartActivity($"Resilience.{policyName}");
        activity?.SetTag(ResilienceTags.Policy, policyName);

        ResilienceDiagnostics.PipelineExecutions.Add(1,
            new KeyValuePair<string, object?>("policy.name", policyName));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var chain = BuildChain(operation);
            var result = await chain(context, ct).ConfigureAwait(false);

            stopwatch.Stop();
            context.TotalElapsed = stopwatch.Elapsed;

            var outcome = context.AttemptNumber > 0 ? "retry" : "success";
            activity?.SetTag(ResilienceTags.Outcome, outcome);
            if (context.AttemptNumber > 0)
                activity?.SetTag(ResilienceTags.Attempt, context.AttemptNumber);
            activity?.SetSuccess();

            ResilienceDiagnostics.PipelineDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("policy.name", policyName),
                new KeyValuePair<string, object?>("outcome", outcome));

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            context.TotalElapsed = stopwatch.Elapsed;

            activity?.RecordException(ex);

            ResilienceDiagnostics.PipelineDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("policy.name", policyName),
                new KeyValuePair<string, object?>("outcome", "exception"));

            throw;
        }
    }

    public async Task ExecuteAsync(
        Func<ResilienceContext, CancellationToken, Task> operation,
        ResilienceContext context,
        CancellationToken ct = default)
    {
        await ExecuteAsync(async (ctx, token) =>
        {
            await operation(ctx, token).ConfigureAwait(false);
            return 0;
        }, context, ct).ConfigureAwait(false);
    }

    /// <remarks>
    ///     Prefer <see cref="DisposeAsync"/> when a strategy is <see cref="IAsyncDisposable"/>-only:
    ///     the sync path must block on its async disposal (no sync alternative exists) and does so
    ///     via a thread-pool hop to avoid sync-context deadlocks. DI containers call DisposeAsync.
    /// </remarks>
    public void Dispose()
    {
        foreach (var strategy in _strategies)
        {
            if (strategy is IDisposable d)
                d.Dispose();
            else if (strategy is IAsyncDisposable asyncDisposable)
                Task.Run(() => asyncDisposable.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var strategy in _strategies)
        {
            if (strategy is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (strategy is IDisposable d)
                d.Dispose();
        }
    }

    private Func<ResilienceContext, CancellationToken, Task<TResult>> BuildChain<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> operation)
    {
        var current = operation;

        for (var i = _strategies.Length - 1; i >= 0; i--)
        {
            var strategy = _strategies[i];
            var next = current;
            current = (ctx, token) => strategy.ExecuteAsync(next, ctx, token);
        }

        return current;
    }
}
