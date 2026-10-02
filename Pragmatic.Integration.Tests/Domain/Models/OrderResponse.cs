namespace Pragmatic.Integration.Tests.Domain.Models;

/// <summary>
///     Response model returned after creating an order.
/// </summary>
public sealed record OrderResponse(Guid OrderId, string Name, decimal Amount, string Status);

/// <summary>
///     Response model returned when getting an order.
/// </summary>
public sealed record OrderDetailResponse(Guid OrderId, string Name, decimal Amount, string Status, DateTimeOffset CreatedAt);
