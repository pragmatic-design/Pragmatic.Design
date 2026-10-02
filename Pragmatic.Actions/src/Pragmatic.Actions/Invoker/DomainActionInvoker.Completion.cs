using System.Diagnostics;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Diagnostics;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Result;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Actions.Invoker;

public abstract partial class DomainActionInvoker<TAction, TReturn>
{
    /// <summary>
    ///     What every answered invocation ends with, whether the body ran or the cache answered: the
    ///     AfterExecute filters, the duration, and the outcome logged and traced.
    /// </summary>
    private async Task<Result<TReturn, IError>> CompleteAsync(
        TAction action,
        Result<TReturn, IError> result,
        List<IActionFilter> filters,
        Activity? activity,
        Stopwatch stopwatch,
        CancellationToken ct)
    {
        var actionName = SActionName;

        // 4. Execute AfterExecute filters (reverse order) — after commit. Also post-commit: a filter
        //    throwing must not flip the reported outcome, so isolate each call.
        for (var i = filters.Count - 1; i >= 0; i--)
        {
            var filter = filters[i];
            var filterName = filter.GetType().Name;
            LogAfterFilter("Action", actionName, filterName);

            try
            {
                await filter.AfterExecuteAsync<TAction, TReturn>(action, result, ct).ConfigureAwait(false);
            }
            catch (Exception afterEx)
            {
                LogPostCommitSideEffectFailed("Action", actionName, afterEx);
                activity?.SetTag(ActionTags.PostCommitFailed, true);
            }
        }

        stopwatch.Stop();

        ActionsDiagnostics.ActionDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("action.name", actionName),
            new KeyValuePair<string, object?>("action.result", result.IsSuccess ? "success" : "failure"));

        if (result.IsSuccess)
        {
            LogSuccess("Action", actionName, stopwatch.ElapsedMilliseconds);
            SetActivitySuccess(activity);
        }
        else
        {
            LogFailure("Action", actionName, result.Error.Code, stopwatch.ElapsedMilliseconds);
            SetActivityFailure(activity, result.Error.Code);

            ActionsDiagnostics.ActionFailures.Add(1,
                new KeyValuePair<string, object?>("action.name", actionName),
                new KeyValuePair<string, object?>("error.code", result.Error.Code));
        }

        return result;
    }
}
