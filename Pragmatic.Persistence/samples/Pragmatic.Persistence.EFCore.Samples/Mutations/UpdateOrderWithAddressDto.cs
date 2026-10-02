using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.Patch;

namespace Pragmatic.Persistence.EFCore.Samples.Mutations;

/// <summary>
///     Patch DTO demonstrating nested patches.
///     When ShippingAddress is provided, it recursively calls ApplyTo on the nested Address.
/// </summary>
[Patch<Order>]
public partial class UpdateOrderWithAddressDto
{
    /// <summary>
    ///     Updated order status.
    /// </summary>
    public OrderStatus? Status { get; init; }

    /// <summary>
    ///     Updated notes.
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    ///     Nested patch for shipping address.
    ///     The generator will call ShippingAddress.ApplyTo(target.ShippingAddress)
    ///     when both DTO and target have non-null values.
    /// </summary>
    public UpdateAddressDto? ShippingAddress { get; init; }
}
