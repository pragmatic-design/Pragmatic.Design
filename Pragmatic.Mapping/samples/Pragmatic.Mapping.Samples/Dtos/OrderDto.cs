using Pragmatic.Mapping.Attributes;
using Pragmatic.Mapping.Samples.Entities;

namespace Pragmatic.Mapping.Samples.Dtos;

// ═══════════════════════════════════════════════════════════════════════════════
// Order DTOs - Demonstrating nested mapping, collections, and projections
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
///     Order DTO with nested customer and collection of lines.
///     Demonstrates nested DTO mapping and collection mapping.
/// </summary>
[MapFrom<Order>]
[GenerateProjection]
public partial record OrderDto
{
    public int Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public decimal Total { get; init; }
    public OrderStatus Status { get; init; }
    public DateTime OrderDate { get; init; }
    public DateTime? ShippedDate { get; init; }

    // Nested DTO mapping - Customer entity -> CustomerDto
    public CustomerDto? Customer { get; init; }

    // Collection of nested DTOs
    public List<OrderLineDto> Lines { get; init; } = [];
}

/// <summary>
///     Customer DTO for nested mapping.
/// </summary>
[MapFrom<Customer>]
[GenerateProjection]
public partial record CustomerDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
}

/// <summary>
///     Order line DTO.
/// </summary>
[MapFrom<OrderLine>]
[GenerateProjection]
public partial record OrderLineDto
{
    public int Id { get; init; }
    public string ProductName { get; init; } = "";
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Flattened Order Summary - Demonstrating flattening nested properties
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
///     Flattened order summary for list views.
///     Demonstrates flattening nested navigation properties.
/// </summary>
[MapFrom<Order>]
[GenerateProjection]
public partial record OrderSummaryDto
{
    public int Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public decimal Total { get; init; }
    public DateTime OrderDate { get; init; }

    // Flattening: Customer.Name -> CustomerName
    public string? CustomerName { get; init; }

    // Flattening: Customer.Email -> CustomerEmail
    public string? CustomerEmail { get; init; }

    // Enum to string conversion with format
    [MapProperty(nameof(Order.Status), Format = "G")]
    public string StatusText { get; init; } = "";
}