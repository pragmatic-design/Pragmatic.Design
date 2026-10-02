namespace Pragmatic.Specification.Samples;

/// <summary>
///     Reusable specifications for User entities.
///     Demonstrates how to organize specifications as static factory methods.
/// </summary>
public static class UserSpecs
{
    /// <summary>
    ///     Users that are active and not deleted.
    /// </summary>
    public static Specification<User> IsActive()
    {
        return Spec<User>.Where(u => u.IsActive && !u.IsDeleted);
    }

    /// <summary>
    ///     Users with a specific role.
    /// </summary>
    public static Specification<User> HasRole(string role)
    {
        return Spec<User>.Where(u => u.Role == role);
    }

    /// <summary>
    ///     Users who are administrators.
    /// </summary>
    public static Specification<User> IsAdmin()
    {
        return HasRole("Admin");
    }

    /// <summary>
    ///     Users with premium subscription.
    /// </summary>
    public static Specification<User> IsPremium()
    {
        return IsActive() & Spec<User>.Where(u => u.SubscriptionLevel == "Premium");
    }

    /// <summary>
    ///     Users who are at least 18 years old.
    /// </summary>
    public static Specification<User> IsAdult()
    {
        return Spec<User>.Where(u => u.Age >= 18);
    }

    /// <summary>
    ///     Users whose name contains the search term.
    /// </summary>
    public static Specification<User> NameContains(string searchTerm)
    {
        return Spec<User>.Where(u => u.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Users belonging to a specific tenant.
    /// </summary>
    public static Specification<User> InTenant(string tenantId)
    {
        return Spec<User>.Where(u => u.TenantId == tenantId);
    }

    /// <summary>
    ///     Active administrators - a common composed specification.
    /// </summary>
    public static Specification<User> IsActiveAdmin()
    {
        return IsActive() & IsAdmin();
    }

    /// <summary>
    ///     Users that are deleted.
    /// </summary>
    public static Specification<User> IsDeleted()
    {
        return Spec<User>.Where(u => u.IsDeleted);
    }
}