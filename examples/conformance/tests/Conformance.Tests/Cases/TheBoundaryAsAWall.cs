using Conformance.Sales.Entities;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     What a relation that crosses the boundary does.
/// </summary>
/// <remarks>
///     <para>
///         <c>Order</c> declares <c>[Relation.ManyToOne&lt;CatalogItem&gt;.WithNavigation("Item")]</c> and
///         <c>CatalogItem</c> belongs to another boundary. The foreign key always crosses; the navigation
///         crosses <b>only if the boundary reads over there</b> — <c>SalesBoundary</c> declares
///         <c>[ReadAccess&lt;CatalogItem&gt;]</c>, so <c>Item</c> exists, read-only.
///     </para>
///     <para>
///         ⚠️ The wall holds by rule, not by the member's absence: a nested write through <c>Item</c> is
///         <c>PRAG0444</c> (parent and child in different boundaries), and the context refuses to commit what
///         it holds only for reading (<see cref="ReadAccessAcrossTheBoundary" />). The case without
///         <c>[ReadAccess]</c>, where the navigation is not generated, lives in the generator tests
///         (<c>CrossBoundaryNavigationTests</c>): here there is a single boundary that reads, and its shape.
///     </para>
/// </remarks>
public class TheBoundaryAsAWall
{
    /// <summary>The foreign key crosses the boundary.</summary>
    [Fact]
    public void ACrossBoundaryRelation_GeneratesTheForeignKey()
    {
        var order = Order.Create();

        order.ItemId.Should().Be(Guid.Empty,
            "`ItemId` exists and is a non-nullable Guid: the column is there, and it starts at zero because "
            + "nobody sets it. The type does not say the link is optional");
    }

    /// <summary>
    ///     The navigation is there because the boundary reads across.
    /// </summary>
    /// <remarks>
    ///     The control is the relation that stays inside the boundary, <c>Lines</c>: the same declaration,
    ///     the same member. What changes across the boundary is not the shape, it is who can write.
    /// </remarks>
    [Fact]
    public void TheNavigation_IsThere_BecauseTheBoundaryReadsAcross()
    {
        var properties = typeof(Order).GetProperties().Select(p => p.Name).ToList();

        properties.Should().Contain("ItemId");
        properties.Should().Contain("Item",
            "SalesBoundary declares [ReadAccess<CatalogItem>]: the navigation is generated, read-only");
        properties.Should().Contain("Lines",
            "while the relation that stays inside the boundary has its navigation as always");
    }
}
