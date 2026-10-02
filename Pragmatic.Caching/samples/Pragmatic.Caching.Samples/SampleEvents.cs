using Pragmatic.Caching.Attributes;

namespace Pragmatic.Caching.Samples;

/// <summary>
///     Sample event with explicit tags and key invalidation.
/// </summary>
[InvalidatesCache("users", "tenant:{TenantId}", Keys = ["user:{UserId}"])]
public partial class UserUpdated
{
    public required int UserId { get; init; }
    public required int TenantId { get; init; }
}

/// <summary>
///     Sample event using convention-based invalidation.
///     "ProductCreated" → invalidates "products" tag.
/// </summary>
[InvalidatesCache]
public partial class ProductCreated
{
    public required int ProductId { get; init; }
    public required string Name { get; init; }
}

/// <summary>
///     Sample event with multiple related tags.
/// </summary>
[InvalidatesCache("orders", "customers", "inventory")]
public partial class OrderPlaced
{
    public required int OrderId { get; init; }
    public required int CustomerId { get; init; }
}