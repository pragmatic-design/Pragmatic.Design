namespace Pragmatic.Specification.Samples;

/// <summary>
///     Shared sample data for all scenarios.
/// </summary>
public static class SampleData
{
    public static List<User> Users { get; } =
    [
        new()
        {
            Id = 1, Name = "Alice", Role = "Admin", IsActive = true, Age = 30, SubscriptionLevel = "Premium",
            TenantId = "tenant-1"
        },
        new()
        {
            Id = 2, Name = "Bob", Role = "User", IsActive = true, Age = 25, SubscriptionLevel = "Free",
            TenantId = "tenant-1"
        },
        new()
        {
            Id = 3, Name = "Charlie", Role = "User", IsActive = false, Age = 35, SubscriptionLevel = "Premium",
            TenantId = "tenant-2"
        },
        new()
        {
            Id = 4, Name = "Diana", Role = "Admin", IsActive = true, Age = 17, SubscriptionLevel = "Free",
            TenantId = "tenant-1"
        },
        new()
        {
            Id = 5, Name = "Eve", Role = "Admin", IsActive = false, IsDeleted = true, Age = 40,
            SubscriptionLevel = "Premium", TenantId = "tenant-2"
        }
    ];

    public static List<Order> Orders { get; } =
    [
        new() { Id = 1, UserId = 1, Amount = 1500m, Status = "Completed", CreatedAt = DateTime.Today.AddDays(-5) },
        new() { Id = 2, UserId = 2, Amount = 250m, Status = "Pending", CreatedAt = DateTime.Today.AddDays(-2) },
        new() { Id = 3, UserId = 1, Amount = 3000m, Status = "Pending", CreatedAt = DateTime.Today.AddDays(-1) },
        new() { Id = 4, UserId = 3, Amount = 500m, Status = "Cancelled", CreatedAt = DateTime.Today.AddDays(-10) },
        new() { Id = 5, UserId = 4, Amount = 1200m, Status = "Pending", CreatedAt = DateTime.Today }
    ];
}