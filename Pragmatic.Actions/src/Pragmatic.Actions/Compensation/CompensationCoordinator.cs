using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Actions.Compensation;

/// <summary>
///     The half of the compensation mechanism that is the same for every invoker: resolving the scope,
///     opening a mark before an invocation, and registering an undo after a commit.
/// </summary>
/// <remarks>
///     A separate object rather than a base-class member because the two pipelines do not share a base:
///     <c>DomainActionInvoker</c> derives from <c>ActionInvokerBase</c> and <c>MutationInvoker</c> does
///     not. Duplicating the resolution in both is how the mutation half would end up subtly different
///     from the action half — which is exactly the shape of defect this whole feature exists to close.
/// </remarks>
public sealed class CompensationCoordinator(IServiceProvider serviceProvider)
{
    private ICompensationScope? _scope;
    private bool _resolved;

    /// <summary>The request's compensation scope, or <c>null</c> when nothing registered one.</summary>
    /// <remarks>
    ///     Absent is the normal case: the generated registration is emitted only where a compensator
    ///     exists, so an application that declares none pays nothing.
    /// </remarks>
    public ICompensationScope? Scope
    {
        get
        {
            if (_resolved)
                return _scope;

            _scope = serviceProvider.GetService<ICompensationScope>();
            _resolved = true;
            return _scope;
        }
    }

    /// <summary>Records where this invocation starts, so it compensates only what it caused.</summary>
    public int Mark() => Scope?.Mark() ?? -1;

    /// <summary>Registers the undo of a step that has just committed.</summary>
    /// <param name="actionName">The action or mutation that committed.</param>
    /// <param name="undo">Runs the compensator and commits it through the caller's unit of work.</param>
    public void Register(string actionName, Func<CancellationToken, Task<Pragmatic.Result.VoidResult<Pragmatic.Result.IError>>> undo)
        => Scope?.Register(new CompensationEntry(actionName, undo));

    /// <summary>Runs the undos registered after <paramref name="mark" />, most recent first.</summary>
    /// <param name="mark">The value returned by <see cref="Mark" />.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>null</c> when there was nothing to undo, or everything undid cleanly.</returns>
    public Task<CompensationFailure?> CompensateFromAsync(int mark, CancellationToken ct)
        => Scope is null ? Task.FromResult<CompensationFailure?>(null) : Scope.CompensateFromAsync(mark, ct);
}
