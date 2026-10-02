using Pragmatic.Mapping.Attributes;
using Pragmatic.Mapping.EFCore.Tests.Entities;

namespace Pragmatic.Mapping.EFCore.Tests.Dtos;

/// <summary>
///     Basic user DTO with flattening.
/// </summary>
[MapFrom<User>]
[GenerateProjection]
public partial record UserDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public bool IsActive { get; init; }

    // Flattening: Address.City -> AddressCity
    public string? AddressCity { get; init; }
    public string? AddressCountry { get; init; }
}

/// <summary>
///     User DTO with full name concatenation.
/// </summary>
[MapFrom<User>]
[GenerateProjection]
public partial record UserSummaryDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";

    // Concatenation: FirstName + LastName
    [MapProperty(nameof(User.FirstName), nameof(User.LastName))]
    public string FullName { get; init; } = "";

    public bool IsActive { get; init; }
}

/// <summary>
///     Order DTO for testing collection projection.
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
}

/// <summary>
///     An order as one row of a list: the number and its status, joined into one cell.
/// </summary>
/// <remarks>
///     ⚠️ The join is over an <b>enum</b>, and that is the point. In memory <c>string + enum</c> calls
///     <c>ToString()</c> and gives the member's name; the same expression translated naively to SQL
///     concatenates the column, which is the number it is stored as — so one declaration would answer
///     <c>ORD-001 · Pending</c> through <c>FromEntity</c> and <c>ORD-001 · 0</c> through the projection.
///     Wrong rather than missing, which is the kind nobody notices.
/// </remarks>
[MapFrom<Order>]
[GenerateProjection]
public partial record OrderRowDto
{
    public int Id { get; init; }

    [MapProperty(nameof(Order.OrderNumber), nameof(Order.Status), Separator = " · ")]
    public string Label { get; init; } = "";
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

/// <summary>
///     An order line as one row: the product and the status of the order it belongs to.
/// </summary>
/// <remarks>
///     ⚠️ The enum is at the end of a <b>dotted</b> path, which is the half the first fix left behind.
///     A part that reaches through a navigation is an enum just as much as one naming a column of this
///     entity, and it was concatenated as its number for exactly as long.
/// </remarks>
[MapFrom<OrderLine>]
[GenerateProjection]
public partial record OrderLineRowDto
{
    public int Id { get; init; }

    [MapProperty(nameof(OrderLine.ProductName), "Order.Status", Separator = " · ")]
    public string Label { get; init; } = "";
}

// =========================================================================
// Nested DTO Projections (P0 Test Coverage)
// =========================================================================

/// <summary>
///     Address DTO for nested projection testing.
/// </summary>
[MapFrom<Address>]
[GenerateProjection]
public partial record AddressDto
{
    public int Id { get; init; }
    public string Street { get; init; } = "";
    public string City { get; init; } = "";
    public string Country { get; init; } = "";
    public string PostalCode { get; init; } = "";
}

/// <summary>
///     User DTO with nested AddressDto (not flattened).
/// </summary>
[MapFrom<User>]
[GenerateProjection]
public partial record UserWithAddressDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public bool IsActive { get; init; }

    // Nested DTO (not flattened)
    public AddressDto? Address { get; init; }
}

/// <summary>
///     Order DTO with nested order lines collection.
/// </summary>
[MapFrom<Order>]
[GenerateProjection]
public partial record OrderWithLinesDto
{
    public int Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public decimal Total { get; init; }
    public OrderStatus Status { get; init; }
    public DateTime OrderDate { get; init; }

    // Nested collection of DTOs
    public List<OrderLineDto> Lines { get; init; } = [];
}

/// <summary>
///     User DTO with nested orders collection.
/// </summary>
[MapFrom<User>]
[GenerateProjection]
public partial record UserWithOrdersDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public bool IsActive { get; init; }

    // Nested collection of DTOs
    public List<OrderDto> Orders { get; init; } = [];
}

/// <summary>
///     Full user DTO with all nested relationships.
/// </summary>
[MapFrom<User>]
[GenerateProjection]
public partial record UserFullDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public bool IsActive { get; init; }

    // Nested single DTO
    public AddressDto? Address { get; init; }

    // Nested collection of DTOs with their own nested collection
    public List<OrderWithLinesDto> Orders { get; init; } = [];
}