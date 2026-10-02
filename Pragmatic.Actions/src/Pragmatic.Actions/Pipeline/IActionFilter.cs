using Pragmatic.Actions.Abstractions;
using Pragmatic.Result;

namespace Pragmatic.Actions.Pipeline;

/// <summary>
///     A filter that runs before and/or after action execution.
///     Filters are executed in order (ascending) for BeforeExecute,
///     and in reverse order (descending) for AfterExecute.
/// </summary>
public interface IActionFilter
{
    /// <summary>
    ///     The order in which this filter runs. Lower values run first.
    ///     Suggested ranges:
    ///     - 100: Validation
    ///     - 200: Authorization
    ///     - 300: Transaction
    ///     - 1000: Logging/Telemetry
    /// </summary>
    int Order { get; }

    /// <summary>
    ///     Runs before the action executes.
    ///     Return a failure result to short-circuit the pipeline.
    /// </summary>
    /// <typeparam name="TAction">The action type.</typeparam>
    /// <typeparam name="TReturn">The return type.</typeparam>
    /// <param name="action">The action about to be executed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success to continue, Failure to short-circuit.</returns>
    Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(
        TAction action,
        CancellationToken ct)
        where TAction : DomainAction<TReturn>;

    /// <summary>
    ///     Runs after the action executes.
    ///     Cannot modify the result, but can perform side effects (logging, metrics).
    /// </summary>
    /// <remarks>
    ///     Runs on BOTH success and failure — that is what makes failure logging/metrics possible.
    ///     A post-processor that assumes success (cache invalidation, notifications, side effects)
    ///     MUST check <c>result.IsSuccess</c> before acting.
    /// </remarks>
    /// <typeparam name="TAction">The action type.</typeparam>
    /// <typeparam name="TReturn">The return type.</typeparam>
    /// <param name="action">The action that was executed.</param>
    /// <param name="result">The result of the execution.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AfterExecuteAsync<TAction, TReturn>(
        TAction action,
        Result<TReturn, IError> result,
        CancellationToken ct)
        where TAction : DomainAction<TReturn>;

    /// <summary>
    ///     Runs before a void action executes.
    ///     Return a failure result to short-circuit the pipeline.
    ///     Default implementation returns success (no-op).
    /// </summary>
    /// <typeparam name="TAction">The void action type.</typeparam>
    /// <param name="action">The action about to be executed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success to continue, Failure to short-circuit.</returns>
    Task<VoidResult<IError>> BeforeExecuteVoidAsync<TAction>(
        TAction action,
        CancellationToken ct)
        where TAction : VoidDomainAction
    {
        return Task.FromResult(VoidResult<IError>.Success());
    }

    /// <summary>
    ///     Runs after a void action executes — on BOTH success and failure
    ///     (check <c>result.IsSuccess</c> before acting on the outcome).
    ///     Cannot modify the result, but can perform side effects (logging, metrics).
    ///     Default implementation is a no-op.
    /// </summary>
    /// <typeparam name="TAction">The void action type.</typeparam>
    /// <param name="action">The action that was executed.</param>
    /// <param name="result">The result of the execution.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AfterExecuteVoidAsync<TAction>(
        TAction action,
        VoidResult<IError> result,
        CancellationToken ct)
        where TAction : VoidDomainAction
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A filter that applies to a specific action type, regardless of return type.
///     Use this for action-specific policies (authorization, validation, business rules).
///     Works with both <see cref="DomainAction{TReturn}" /> and <see cref="VoidDomainAction" />.
/// </summary>
/// <typeparam name="TAction">The action type this filter applies to.</typeparam>
public interface IActionFilter<TAction>
{
    /// <summary>
    ///     Execution order. Lower values run first.
    ///     Suggested: 200 for authorization policies.
    /// </summary>
    int Order => 200;

    /// <summary>
    ///     Runs before the action executes.
    ///     Return a failure result to short-circuit the pipeline.
    /// </summary>
    Task<VoidResult<IError>> BeforeExecuteAsync(TAction action, CancellationToken ct);

    /// <summary>
    ///     Runs after the action executes (optional). Default: no-op.
    /// </summary>
    Task AfterExecuteAsync(TAction action, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
///     A filter that applies only to a specific action type with a known return type.
///     Use this for action-specific filtering logic that needs access to the result.
/// </summary>
/// <typeparam name="TAction">The action type this filter applies to.</typeparam>
/// <typeparam name="TReturn">The return type of the action.</typeparam>
public interface IActionFilter<TAction, TReturn>
    where TAction : DomainAction<TReturn>
{
    /// <inheritdoc cref="IActionFilter.Order" />
    int Order { get; }

    /// <summary>
    ///     Runs before the action executes.
    /// </summary>
    Task<VoidResult<IError>> BeforeExecuteAsync(TAction action, CancellationToken ct);

    /// <summary>
    ///     Runs after the action executes — on BOTH success and failure.
    ///     Check <c>result.IsSuccess</c> before acting on the outcome.
    /// </summary>
    Task AfterExecuteAsync(TAction action, Result<TReturn, IError> result, CancellationToken ct);
}
