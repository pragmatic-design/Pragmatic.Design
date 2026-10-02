namespace Pragmatic.Mapping.EFCore.Tests.Entities;

/// <summary>
///     User entity for testing mapping scenarios.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }

    // Navigation properties
    public Address? Address { get; set; }
    public List<Order> Orders { get; set; } = [];
}

/// <summary>
///     Address entity for testing nested mapping.
/// </summary>
public class Address
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
    public string PostalCode { get; set; } = "";

    public User User { get; set; } = null!;
}

/// <summary>
///     Order entity for testing collection mapping.
/// </summary>
public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string OrderNumber { get; set; } = "";
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime OrderDate { get; set; }

    public User User { get; set; } = null!;
    public List<OrderLine> Lines { get; set; } = [];
}

/// <summary>
///     Order line for testing nested collection mapping.
/// </summary>
public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public Order Order { get; set; } = null!;
}

public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}