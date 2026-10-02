using Pragmatic.Caching.Attributes;

namespace Pragmatic.Caching.Samples;

/// <summary>
///     Sample query with basic [Cacheable] usage.
/// </summary>
[Cacheable(Duration = "5m")]
public partial class GetUser
{
    public required int UserId { get; init; }
}

/// <summary>
///     Sample query with multiple properties in cache key.
///     PageSize is excluded from the key.
/// </summary>
[Cacheable(Duration = "10m")]
public partial class GetUserOrders
{
    public required int TenantId { get; init; }
    public required int UserId { get; init; }

    [CacheKey(Exclude = true)] public int PageSize { get; init; } = 20;
}

/// <summary>
///     Sample query with tag placeholders.
///     Tags expand at runtime to include the actual TenantId.
/// </summary>
[Cacheable(Duration = "5m", Tags = ["users", "tenant:{TenantId}"])]
public partial class GetActiveUsers
{
    public required int TenantId { get; init; }
}

/// <summary>
///     Sample query with custom key ordering.
/// </summary>
[Cacheable(Duration = "1h")]
public partial class GetProductDetails
{
    [CacheKey(Order = 1, Name = "store")] public required int StoreId { get; init; }

    [CacheKey(Order = 2, Name = "product")]
    public required int ProductId { get; init; }
}

/// <summary>
///     Sample query with sliding expiration.
/// </summary>
[Cacheable(Duration = "30m", Sliding = true)]
public partial class GetDashboardStats
{
    public required int UserId { get; init; }
}