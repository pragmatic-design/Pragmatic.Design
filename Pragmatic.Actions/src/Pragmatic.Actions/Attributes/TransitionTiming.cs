namespace Pragmatic.Actions.Attributes;

/// <summary>
///     When the invoker performs the transition <see cref="TransitionsToAttribute{TState}" /> declares.
/// </summary>
public enum TransitionTiming
{
    /// <summary>
    ///     After the entity is loaded and before the body runs. The body sees the new state, and a
    ///     transition the state machine refuses answers <c>409</c> before any of the body's work is done.
    ///     The default, and the only timing right for a domain action, which builds its response in the
    ///     body.
    /// </summary>
    BeforeBody = 0,

    /// <summary>
    ///     After a successful body and before validation, invariants and the save. The body's own
    ///     refusals keep their own errors, and the transition's events see what the body wrote. Mutations
    ///     only: a domain action has already built its response when the body returns.
    /// </summary>
    AfterBody = 1,

    /// <summary>
    ///     The body performs the transition — typically inside a domain method that writes the state
    ///     together with who, when and why. The invoker does not transition; after the body it checks that
    ///     the state is the declared one, unless the transition is
    ///     <see cref="TransitionsToAttribute{TState}.IsConditional" />.
    /// </summary>
    ByBody = 2
}
