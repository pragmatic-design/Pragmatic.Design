namespace Pragmatic.Persistence.StateMachine;

/// <summary>
///     Declares that a state transition should raise a domain event of the specified type.
///     Applied to enum values decorated with <see cref="TransitionFromAttribute" />.
/// </summary>
/// <typeparam name="TEvent">The domain event type to raise on this transition.</typeparam>
/// <remarks>
///     <para>
///         When <c>TransitionTo()</c> succeeds, the generated code will enqueue
///         an instance of <typeparamref name="TEvent" /> to the entity's domain events collection.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public enum ReservationStatus
/// {
///     [InitialState]
///     Pending,
///
///     [TransitionFrom(ReservationStatus.Pending)]
///     [RaisesEvent&lt;ReservationConfirmedEvent&gt;]
///     Confirmed
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = true)]
public sealed class RaisesEventAttribute<TEvent> : Attribute
    where TEvent : class;
