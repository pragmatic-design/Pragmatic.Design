namespace Pragmatic.Persistence.Samples.Entities;

/// <summary>
///     Sample OrderLine entity for demonstrating Query features.
/// </summary>
public class OrderLine
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string ProductName { get; set; } = "";
    public string ProductSku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    // Navigation property
    public Order? Order { get; set; }
}
