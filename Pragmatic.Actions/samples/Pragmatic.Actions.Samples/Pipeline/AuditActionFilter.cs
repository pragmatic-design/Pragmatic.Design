using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Actions.Pipeline.Filters;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Pipeline;

/// <summary>
///     A custom global <see cref="IActionFilter" /> that records an audit trail around every action.
///     Demonstrates how to plug a cross-cutting filter into the Pragmatic action pipeline.
/// </summary>
/// <remarks>
///     <para>
///         Register globally with <c>services.AddSingleton&lt;IActionFilter, AuditActionFilter&gt;()</c>
///         (use <c>TryAddEnumerable</c> in production to avoid duplicate registration). The pipeline
///         runs BeforeExecute filters in ascending <see cref="Order" /> and AfterExecute in descending.
///     </para>
///     <para>
///         <see cref="Order" /> is 900 — just before the built-in logging filter (1000), so audit
///         entries bracket the innermost business logic. Returning a failure from
///         <see cref="BeforeExecuteAsync{TAction,TReturn}" /> would short-circuit the pipeline.
///     </para>
/// </remarks>
public sealed class AuditActionFilter : IActionFilter
{
    private readonly List<string> _trail;

    /// <param name="trail">A shared sink the sample uses to display what the filter captured.</param>
    public AuditActionFilter(List<string> trail) => _trail = trail;

    /// <inheritdoc />
    public int Order => FilterOrder.Logging - 100; // 900

    /// <inheritdoc />
    public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(
        TAction action, CancellationToken ct)
        where TAction : DomainAction<TReturn>
    {
        _trail.Add($"[audit] → entering {typeof(TAction).Name}");
        return Task.FromResult(VoidResult<IError>.Success());
    }

    /// <inheritdoc />
    public Task AfterExecuteAsync<TAction, TReturn>(
        TAction action, Result<TReturn, IError> result, CancellationToken ct)
        where TAction : DomainAction<TReturn>
    {
        _trail.Add($"[audit] ← exiting {typeof(TAction).Name} (success={result.IsSuccess})");
        return Task.CompletedTask;
    }
}
