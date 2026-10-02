using Pragmatic.Mapping.Attributes;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Dtos;

/// <summary>
///     A mapping path that crosses the boundary — the half «with <c>[ReadAccess]</c>».
/// </summary>
/// <remarks>
///     <para>
///         <c>Order.Item</c> is generated because <c>SalesBoundary</c> declares
///         <c>[ReadAccess&lt;CatalogItem&gt;]</c>, and a <c>[MapProperty("Item.Name")]</c> crosses it like
///         any other navigation: the read list carries <c>Item</c>, and the projection JOINs on the
///         <c>DbSet</c> the attribute adds. Without that line on the boundary this same attribute would
///         be <c>PRAG0334</c>.
///     </para>
///     <para>
///         ⚠️ A separate DTO, not a property on <c>OrderDto</c>: <c>ItemId</c> starts at zero and the
///         relation is required, so a projection going through <c>Item</c> would drop from the responses
///         every order without an item. <c>ThePathAcrossTheBoundary</c> measures it, assigning the item
///         before reading.
///     </para>
/// </remarks>
[MapFrom<Order>]
[GenerateProjection]
public partial record OrderWithItemDto
{
    public Guid Id { get; init; }

    public string Reference { get; init; } = "";

    [MapProperty("Item.Name")]
    public string ItemName { get; init; } = "";
}
