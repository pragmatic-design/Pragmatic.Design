namespace Pragmatic.Persistence.StateMachine;

/// <summary>
///     Declares that a state can be reached from the specified source states.
///     Applied to enum values to define the legal transitions in a state machine.
/// </summary>
/// <remarks>
///     <para>
///         Multiple attributes can be applied to allow transitions from multiple states.
///         The source generator uses these to build a compile-time transition table
///         and generate <c>TransitionTo()</c> and <c>CanTransitionTo()</c> methods.
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
///     Confirmed,
///
///     [TransitionFrom(ReservationStatus.Confirmed)]
///     CheckedIn,
///
///     [TransitionFrom(ReservationStatus.Pending)]
///     [TransitionFrom(ReservationStatus.Confirmed)]
///     Cancelled
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = true)]
public sealed class TransitionFromAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance with the source state value.
    /// </summary>
    /// <param name="fromState">The state from which this transition is allowed.</param>
    public TransitionFromAttribute(object fromState)
    {
        FromState = fromState;
    }

    /// <summary>
    ///     Gets the source state from which the transition is allowed.
    /// </summary>
    public object FromState { get; }
}
