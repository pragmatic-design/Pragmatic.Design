using Pragmatic.Result;

namespace Pragmatic.Actions.Invoker;

public abstract partial class DomainActionInvoker<TAction, TReturn>
{
    /// <summary>
    ///     Validates the field rules (<c>[Required]</c>, <c>[MaxLength]</c>, …) of each entity this operation loaded and changed, and returns
    ///     the first failure. Overridden by the generated invoker; null means there is nothing to validate.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Asked after the body and before the write, before the <c>[Invariant]</c> rules: the order the mutation pipeline validates the
    ///         entity it writes. Without it an action that sets a field writes a value no rule was asked of.
    ///     </para>
    ///     <para>
    ///         "Changed" is the unit of work's answer (<see cref="Pragmatic.Persistence.Repository.IUnitOfWork.IsModified" />): a row loaded
    ///         and left alone is not validated, so a stored value that already breaks a rule does not refuse an action that did not write it.
    ///     </para>
    /// </remarks>
    /// <param name="action">The operation, holding the entities its preload assigned.</param>
    protected virtual IError? ValidateLoadedEntities(TAction action) => null;
}
