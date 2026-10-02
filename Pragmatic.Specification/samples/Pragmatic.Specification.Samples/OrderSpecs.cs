namespace Pragmatic.Specification.Samples;

/// <summary>
///     Reusable specifications for Order entities.
/// </summary>
public static class OrderSpecs
{
    /// <summary>
    ///     Orders with a specific status.
    /// </summary>
    public static Specification<Order> HasStatus(string status)
    {
        return Spec<Order>.Where(o => o.Status == status);
    }

    /// <summary>
    ///     Pending orders.
    /// </summary>
    public static Specification<Order> IsPending()
    {
        return HasStatus("Pending");
    }

    /// <summary>
    ///     Completed orders.
    /// </summary>
    public static Specification<Order> IsCompleted()
    {
        return HasStatus("Completed");
    }

    /// <summary>
    ///     Orders above a minimum amount.
    /// </summary>
    public static Specification<Order> AmountGreaterThan(decimal amount)
    {
        return Spec<Order>.Where(o => o.Amount > amount);
    }

    /// <summary>
    ///     Orders created within a date range.
    /// </summary>
    public static Specification<Order> CreatedBetween(DateTime start, DateTime end)
    {
        return Spec<Order>.Where(o => o.CreatedAt >= start && o.CreatedAt <= end);
    }

    /// <summary>
    ///     Orders belonging to a specific user.
    /// </summary>
    public static Specification<Order> BelongsToUser(int userId)
    {
        return Spec<Order>.Where(o => o.UserId == userId);
    }

    /// <summary>
    ///     High-value pending orders (common business rule).
    /// </summary>
    public static Specification<Order> IsHighValuePending(decimal threshold = 1000m)
    {
        return IsPending() & AmountGreaterThan(threshold);
    }
}