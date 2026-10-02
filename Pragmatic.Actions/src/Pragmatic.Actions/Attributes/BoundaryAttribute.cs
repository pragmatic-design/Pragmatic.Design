namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Defines a boundary that groups DomainActions into an interface.
///     The boundary captures all DomainActions in its namespace and sub-namespaces.
/// </summary>
/// <remarks>
///     <para>
///         Boundaries follow Interface Segregation Principle (ISP) by creating
///         focused interfaces from namespace-based grouping.
///     </para>
///     <para>
///         Capture rules:
///         - A Boundary captures its namespace AND all sub-namespaces (deep capture)
///         - If a sub-namespace has its own Boundary, it "subtracts" from the parent
///         - Actions with Internal=true or System=true are excluded
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // In Orders/OrdersBoundary.cs
/// [Boundary]
/// public partial class OrdersBoundary { }
///
/// // Generates: IOrdersActions with all actions in Orders.* namespace
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BoundaryAttribute : Attribute
{
    /// <summary>
    ///     Optional custom name for the generated interface.
    ///     If not specified, the interface name is derived from the boundary class name.
    /// </summary>
    /// <remarks>
    ///     Examples:
    ///     - OrdersBoundary → IOrdersActions (default)
    ///     - OrdersBoundary with Name="IOrderCommands" → IOrderCommands
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    ///     Controls the visibility of the generated boundary interface.
    ///     Default is <see cref="BoundaryVisibility.Public"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="BoundaryVisibility.Internal"/>: only the internal interface is generated.
    ///         Actions are callable within the same assembly via intra-boundary calls,
    ///         but cannot be exposed via endpoints or used remotely.
    ///     </para>
    /// </remarks>
    public BoundaryVisibility Visibility { get; set; } = BoundaryVisibility.Public;
}
