namespace Pragmatic.Specification.Samples;

/// <summary>
///     Sample user entity for demonstrating specifications.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string SubscriptionLevel { get; set; } = "Free";
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public int Age { get; set; }
    public string? TenantId { get; set; }

    public override string ToString()
    {
        return $"User {{ Id={Id}, Name={Name}, Role={Role}, Active={IsActive}, Age={Age} }}";
    }
}

/// <summary>
///     Sample order entity for demonstrating specifications.
/// </summary>
public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }

    public override string ToString()
    {
        return $"Order {{ Id={Id}, UserId={UserId}, Amount={Amount:C}, Status={Status} }}";
    }
}