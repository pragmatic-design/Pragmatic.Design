namespace Pragmatic.Actions.Attributes;

/// <summary>
///     The operation moves its entity's state machine to <paramref name="target" />, and the generated
///     invoker performs the move — the body does not call <c>TransitionTo</c>.
/// </summary>
/// <typeparam name="TState">The state-machine enum: the entity's <c>[StateMachine&lt;TState&gt;]</c>.</typeparam>
/// <param name="target">The state the entity ends in.</param>
/// <remarks>
///     <para>
///         The entity is the mutation's own, or — on a domain action — the one loaded with
///         <c>[LoadEntity]</c> whose state machine is <typeparamref name="TState" />. A refused transition
///         answers <c>409</c>, which the generated endpoint documents.
///     </para>
///     <para>
///         The target is written once, here, and the generated invoker either performs the
///         transition or checks that the body did. With <see cref="When" /> left at
///         <see cref="TransitionTiming.BeforeBody" /> or set to <see cref="TransitionTiming.AfterBody" />, a
///         body that still calls <c>TransitionTo(target)</c> is <c>PRAG0466</c>: the second call would be
///         refused as a transition from the target to itself.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Mutation(Mode = MutationMode.Update)]
/// [TransitionsTo&lt;ReservationStatus&gt;(ReservationStatus.Confirmed)]
/// public partial class ConfirmReservationMutation : Mutation&lt;Reservation&gt;
/// {
///     public required Guid Id { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TransitionsToAttribute<TState>(TState target) : Attribute
    where TState : struct, Enum
{
    /// <summary>The state the operation moves the entity into.</summary>
    public TState Target { get; } = target;

    /// <summary>When the invoker performs the transition. <see cref="TransitionTiming.BeforeBody" /> by default.</summary>
    public TransitionTiming When { get; set; } = TransitionTiming.BeforeBody;

    /// <summary>
    ///     With <see cref="TransitionTiming.ByBody" />: the body moves the entity only under a condition of
    ///     its own — a payment that settles the invoice, not every payment — so the invoker does not check
    ///     the final state. The declaration still names the transition for the contract tests.
    /// </summary>
    public bool IsConditional { get; set; }
}
