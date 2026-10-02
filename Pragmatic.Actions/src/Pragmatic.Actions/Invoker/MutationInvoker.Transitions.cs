using Pragmatic.Result;

namespace Pragmatic.Actions.Invoker;

public abstract partial class MutationInvoker<TMutation, TEntity>
{
    /// <summary>
    ///     The transition <c>[TransitionsTo]</c> declares with <c>BeforeBody</c>: performed after the load
    ///     and before <c>ApplyAsync</c>. Overridden by the generated invoker.
    /// </summary>
    /// <returns>The state machine's refusal — a <c>409</c> — or <c>null</c>.</returns>
    protected virtual IError? TransitionBeforeBody(TEntity entity) => null;

    /// <summary>
    ///     The transition <c>[TransitionsTo]</c> declares with <c>AfterBody</c>: performed after a
    ///     successful <c>ApplyAsync</c> and before validation, invariants and the save. Overridden by the
    ///     generated invoker.
    /// </summary>
    protected virtual IError? TransitionAfterBody(TEntity entity) => null;

    /// <summary>
    ///     With <c>ByBody</c>: throws when the body left the entity in a state other than the declared
    ///     one. Overridden by the generated invoker.
    /// </summary>
    /// <remarks>
    ///     A throw and not a 409: the declaration and the code disagree, which is a programming error the
    ///     caller cannot fix by trying again.
    /// </remarks>
    protected virtual void EnsureTheBodyTransitioned(TEntity entity)
    {
    }
}
