using Pragmatic.Actions.Abstractions;
using Pragmatic.Result;

namespace Pragmatic.Actions.Invoker;

/// <summary>
///     Invokes a DomainAction with dependency injection and pipeline execution.
/// </summary>
/// <typeparam name="TAction">The action type.</typeparam>
/// <typeparam name="TReturn">The return type.</typeparam>
/// <remarks>
///     This interface is implemented by generated invoker classes.
///     Each action gets its own invoker that handles dependency injection
///     and pipeline execution.
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IDomainActionInvoker<in TAction, TReturn>
    where TAction : DomainAction<TReturn>
{
    /// <summary>
    ///     Invokes the action through the pipeline.
    /// </summary>
    /// <param name="action">The action instance with parameters set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the action execution.</returns>
    Task<Result<TReturn, IError>> InvokeAsync(TAction action, CancellationToken ct = default);
}
