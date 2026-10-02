using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Boundary;
using Pragmatic.Actions.Samples.Entities;

namespace Pragmatic.Actions.Samples.Boundaries;

/// <summary>
///     Inventory bounded context marker. Demonstrates the SHAPE of a boundary that would use
///     <see cref="BoundaryVisibility.Internal" /> and cross-boundary <c>[ReadAccess&lt;Order&gt;]</c>.
/// </summary>
/// <remarks>
///     <para>
///         The intended declaration is shown in the doc-comment below. It is NOT applied as live
///         attributes here because, in this single-assembly samples project, adding a <c>[Boundary]</c>
///         makes the source generator capture every action in the assembly — including
///         <c>VersionedCreateOrderAction</c>, whose enum-typed parameter trips a generator bug
///         (see "BUGS FOUND" in the sample report). The attribute semantics are exercised instead by
///         inspecting the real attribute types in <c>BoundaryVisibilitySample</c>.
///     </para>
///     <para>
///         Intended declaration:
///         <code>
///         [Boundary(Visibility = BoundaryVisibility.Internal)]
///         [ReadAccess&lt;Order&gt;]
///         public partial class InventoryBoundary { }
///         </code>
///     </para>
/// </remarks>
public sealed class InventoryBoundary : IBoundary
{
    /// <summary>
    ///     The visibility this boundary would declare. Kept as data so the sample can print a real
    ///     <see cref="BoundaryVisibility" /> value without applying the (bug-triggering) attribute.
    /// </summary>
    public static BoundaryVisibility IntendedVisibility => BoundaryVisibility.Internal;

    /// <summary>
    ///     The entity this boundary would declare read access to via <c>[ReadAccess&lt;Order&gt;]</c>.
    /// </summary>
    public static Type ReadAccessEntity => typeof(Order);
}
