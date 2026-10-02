namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Specifies which saga state(s) a handler method is valid for.
///     The SG uses this to deduce the state transition graph.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class InStateAttribute(object state) : Attribute
{
    /// <summary>The state value this handler responds to.</summary>
    public object State { get; } = state;

    /// <summary>The state to transition to after successful execution.</summary>
    public object? NextState { get; set; }
}
