namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Marks a facade method whose action declares <c>[UndoWith&lt;T&gt;]</c>, so a caller in another
///     boundary knows this step undoes itself.
/// </summary>
/// <remarks>
///     Emitted by the generator, never written by hand. It is what lets PRAG0424 be precise about which
///     steps a caller actually invokes: the alternative — one flag on the facade meaning "every action
///     here is compensable" — would demand a compensator for operations the caller never calls, and the
///     first real boundary would fail that bar for a reason unrelated to the caller's risk.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CompensableStepAttribute : Attribute;
