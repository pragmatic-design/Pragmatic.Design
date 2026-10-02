namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Marks a generated boundary facade — the interface through which one boundary calls another's
///     actions.
/// </summary>
/// <typeparam name="TBoundary">The boundary the facade belongs to.</typeparam>
/// <remarks>
///     Emitted by the generator, never written by hand. It exists so a cross-boundary call is
///     recognisable by a marker rather than by the shape of a name: the alternative was matching
///     <c>I{Something}Actions</c>, which is a convention the compiler cannot check and which any
///     hand-written interface could imitate.
/// </remarks>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class BoundaryActionsAttribute<TBoundary> : Attribute;
