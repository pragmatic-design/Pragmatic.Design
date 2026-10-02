using Pragmatic.Validation.Attributes;

namespace Pragmatic.Validation.Samples;

/// <summary>
///     Demonstrates nested validation of a collection's elements.
/// </summary>
/// <remarks>
///     Each item is validated individually by the generated code, with indexed paths — and no
///     attribute asks for it: an element type that is an <c>ISyncValidator</c> is enough.
///     A shopper's basket collects every problem at once, so there is nothing here to
///     stop early for.
/// </remarks>
public partial record CreateOrderRequest
{
    [Required]
    public required string CustomerId { get; init; }

    [Required, MinCount(1), MaxCount(50)]
    public required List<OrderItemRequest> Items { get; init; }

    [MaxLength(500)]
    public string? Notes { get; init; }
}
