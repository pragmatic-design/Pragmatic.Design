using Pragmatic.Result;

namespace Pragmatic.Actions.Invoker;

public abstract partial class VoidDomainActionInvoker<TAction>
{
    /// <summary>
    ///     Checks the <c>[Invariant]</c> rules of the entities this operation loaded, and returns the
    ///     first violation. Overridden by the generated invoker; null means there is nothing to check.
    /// </summary>
    /// <remarks>
    ///     The same member as the typed invoker's, for the same generated override: only the
    ///     rules the operation can answer — those reading no navigation outside its <c>Include</c> list.
    /// </remarks>
    /// <param name="action">The operation, holding the entities its preload assigned.</param>
    protected virtual IError? CheckLoadedInvariants(TAction action) => null;
}
