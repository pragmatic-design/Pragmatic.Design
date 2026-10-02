using Pragmatic.Actions.Abstractions;
using Pragmatic.Result;

namespace Pragmatic.Actions.Invoker;

/// <summary>
///     Invokes a VoidDomainAction with dependency injection and pipeline execution.
/// </summary>
/// <typeparam name="TAction">The void action type.</typeparam>
/// <remarks>
///     This interface is implemented by generated invoker classes.
///     Each void action gets its own invoker that handles dependency injection
///     and pipeline execution.
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IVoidDomainActionInvoker<in TAction>
    where TAction : IVoidExecutable
{
    /// <summary>
    ///     Invokes the void action through the pipeline.
    /// </summary>
    /// <param name="action">The action instance with parameters set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A VoidResult indicating success or containing an error.</returns>
    Task<VoidResult<IError>> InvokeAsync(TAction action, CancellationToken ct = default);
}
