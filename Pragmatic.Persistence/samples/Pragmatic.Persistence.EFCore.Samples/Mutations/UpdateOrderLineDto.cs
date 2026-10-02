using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.Patch;

namespace Pragmatic.Persistence.EFCore.Samples.Mutations;

/// <summary>
///     Patch DTO for updating an order line.
///     Used as an element in collection patches.
/// </summary>
[Patch<OrderLine>]
public partial class UpdateOrderLineDto
{
    /// <summary>
    ///     Order line ID for matching existing lines.
    /// </summary>
    public Guid? Id { get; init; }

    /// <summary>
    ///     Updated quantity.
    /// </summary>
    public int? Quantity { get; init; }

    /// <summary>
    ///     Updated unit price.
    /// </summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>
    ///     Updated discount percentage.
    /// </summary>
    public decimal? DiscountPercent { get; init; }
}
