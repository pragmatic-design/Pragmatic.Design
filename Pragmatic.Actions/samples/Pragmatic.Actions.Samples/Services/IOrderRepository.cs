namespace Pragmatic.Actions.Samples.Services;

/// <summary>
///     Sample repository interface for order operations.
/// </summary>
public interface IOrderRepository
{
    Task<OrderRecord?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<OrderRecord> CreateAsync(string product, int quantity, CancellationToken ct);
    Task<IReadOnlyList<OrderRecord>> SearchAsync(string query, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetIdsByUserAsync(Guid userId, Guid? tenantId, CancellationToken ct);
}

/// <summary>
///     Simple order record for sample purposes.
/// </summary>
public sealed record OrderRecord(Guid Id, string Product, int Quantity, DateTimeOffset CreatedAt);
