using Pragmatic.Validation.Attributes;

namespace Pragmatic.Validation.Samples;

/// <summary>
///     Element type validated via [ValidateElements] on the parent collection.
///     Must be partial and have validation attributes to support element validation.
/// </summary>
public partial record OrderItemRequest
{
    [Required]
    public required string ProductId { get; init; }

    [Positive]
    public required int Quantity { get; init; }

    [Range(0.01, 99999.99)]
    public required decimal UnitPrice { get; init; }
}
