namespace Pragmatic.Persistence.StateMachine;

/// <summary>
///     Marks an enum value as the initial state for a state machine.
///     Every state machine enum must have exactly one value with this attribute.
/// </summary>
/// <example>
///     <code>
/// public enum ReservationStatus
/// {
///     [InitialState]
///     Pending,
///
///     [TransitionFrom(ReservationStatus.Pending)]
///     Confirmed,
///     ...
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class InitialStateAttribute : Attribute;
