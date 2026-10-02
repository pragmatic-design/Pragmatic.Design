using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.EFCore.Samples.Converters;
using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.Patch;

namespace Pragmatic.Persistence.EFCore.Samples.Mutations;

/// <summary>
///     Patch DTO demonstrating [MapConverter] usage.
///     Shows how to convert between different types during patching.
/// </summary>
[Patch<Order>]
public partial class OrderMutationWithConvertersDto
{
    /// <summary>
    ///     Status as string (e.g., "Shipped", "Pending").
    ///     Converter transforms to OrderStatus enum.
    /// </summary>
    [MapConverter<OrderStatusToStringConverter>]
    public string? Status { get; init; }

    /// <summary>
    ///     Standard notes mapping.
    /// </summary>
    public string? Notes { get; init; }
}

/// <summary>
///     Customer patch demonstrating multiple converters.
/// </summary>
[Patch<Customer>]
public partial class CustomerMutationWithConvertersDto
{
    /// <summary>
    ///     Customer name - standard mapping.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    ///     Customer type as integer code.
    ///     0 = Individual, 1 = Business, 2 = Enterprise
    /// </summary>
    [MapConverter<CustomerTypeToIntConverter>]
    public int? Type { get; init; }

    /// <summary>
    ///     Active status as "Y" or "N" string.
    ///     Common in legacy integrations.
    /// </summary>
    [MapConverter<BoolToYesNoConverter>]
    public string? IsActive { get; init; }
}
