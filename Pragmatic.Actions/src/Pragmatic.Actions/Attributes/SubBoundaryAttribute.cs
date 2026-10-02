namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Marks a class as a sub-boundary grouping within a parent <see cref="BoundaryAttribute">Boundary</see>.
///     Sub-boundaries generate their own interface and local implementation, composed into the root via property.
///     When not explicitly specified, sub-boundaries are inferred from namespace structure.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SubBoundaryAttribute : Attribute
{
    /// <summary>
    ///     Optional custom name for the sub-boundary. If not specified, inferred from namespace.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    ///     Optional description for the sub-boundary.
    /// </summary>
    public string? Description { get; set; }
}
