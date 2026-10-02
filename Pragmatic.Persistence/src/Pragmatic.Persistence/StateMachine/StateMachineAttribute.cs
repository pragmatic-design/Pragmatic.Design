namespace Pragmatic.Persistence.StateMachine;

/// <summary>
///     Marks an entity as having a state machine driven by the specified enum type.
///     The source generator will generate <c>TransitionTo</c>, <c>CanTransitionTo</c>,
///     and <c>AllowedTransitions</c> methods on the entity.
/// </summary>
/// <typeparam name="TEnum">The enum type representing the states.</typeparam>
/// <remarks>
///     <para>
///         The enum values must be decorated with <see cref="TransitionFromAttribute" /> to define
///         legal transitions. The generator uses these to build a compile-time transition table.
///     </para>
///     <para>
///         The entity must have a property of type <typeparamref name="TEnum" /> with a generated
///         setter (private set) so that transitions are tracked in <c>_modifiedProperties</c>.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Entity]
/// [StateMachine&lt;ReservationStatus&gt;]
/// public partial class Reservation : IEntity
/// {
///     public ReservationStatus Status { get; private set; }
/// }
/// </code>
/// </example>
// AllowMultiple = false: the generator only ever consumes one state machine per entity, so a
// second [StateMachine<>] would be silently ignored; making the attribute non-repeatable turns
// that into a compile error the developer actually sees.
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class StateMachineAttribute<TEnum> : Attribute
    where TEnum : struct, Enum
{
    /// <summary>
    ///     Gets or sets the name of the entity property that holds the state.
    ///     If not specified, defaults to "Status".
    /// </summary>
    public string Property { get; set; } = "Status";
}
