using Pragmatic.Result;

namespace Pragmatic.Actions.Invoker;

public abstract partial class DomainActionInvoker<TAction, TReturn>
{
    /// <summary>
    ///     The transition <c>[TransitionsTo]</c> declares with <c>BeforeBody</c>, on the entity loaded with
    ///     <c>[LoadEntity]</c>: performed after the loads and before <c>Execute</c>. Overridden by the
    ///     generated invoker.
    /// </summary>
    /// <returns>The state machine's refusal — a <c>409</c> — or <c>null</c>.</returns>
    protected virtual IError? TransitionBeforeBody(TAction action) => null;

    /// <summary>
    ///     With <c>ByBody</c>: throws when a successful body left the entity in a state other than the
    ///     declared one. Overridden by the generated invoker.
    /// </summary>
    /// <remarks>A throw and not a 409: the declaration and the code disagree, which no retry fixes.</remarks>
    protected virtual void EnsureTheBodyTransitioned(TAction action)
    {
    }
}
