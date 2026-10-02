namespace Pragmatic.Persistence.Samples.Entities;

/// <summary>
///     Sample Customer entity for demonstrating Query features.
/// </summary>
public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public CustomerType Type { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public List<Order> Orders { get; set; } = [];
}

public enum CustomerType
{
    Individual,
    Business,
    Enterprise,
    Government
}
