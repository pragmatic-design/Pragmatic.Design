namespace Pragmatic.Persistence.Samples.Entities;

/// <summary>
///     Sample Order entity for demonstrating Query features.
/// </summary>
public class Order
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public Guid CustomerId { get; set; }

    // Navigation properties (for EF Core scenarios)
    public Customer? Customer { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}

public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}
