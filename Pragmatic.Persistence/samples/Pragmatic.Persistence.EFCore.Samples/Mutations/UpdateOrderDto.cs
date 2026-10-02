using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.Patch;

namespace Pragmatic.Persistence.EFCore.Samples.Mutations;

/// <summary>
///     Patch DTO for updating an order.
///     Demonstrates:
///     - [Patch] attribute for generating ApplyTo method
///     - Nullable properties for partial updates
///     - Collection patches with [CollectionStrategy]
/// </summary>
[Patch<Order>]
public partial class UpdateOrderDto
{
    /// <summary>
    ///     Updated order status.
    /// </summary>
    public OrderStatus? Status { get; init; }

    /// <summary>
    ///     Updated notes.
    /// </summary>
    public string? Notes { get; init; }
}
