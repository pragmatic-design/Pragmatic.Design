namespace Showcase.Booking.Infrastructure.Services;

/// <summary>
/// Calculates pricing for reservations.
/// </summary>
public interface IReservationPricingService
{
    /// <summary>Calculates the total amount for a stay.</summary>
    Task<decimal> CalculateTotalAsync(
        Guid roomTypeId,
        DateTimeOffset checkIn,
        DateTimeOffset checkOut,
        CancellationToken ct = default);
}
