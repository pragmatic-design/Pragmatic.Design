namespace Warehouse.Stock.Infrastructure.Jobs;

/// <summary>What <see cref="ExpireReservationsJob" /> is told: the order whose holds are due.</summary>
public sealed record ExpireReservations(Guid OrderId);
