using Microsoft.Extensions.Logging;

namespace Showcase.Booking.Infrastructure.Services;

/// <summary>
/// Logs pricing calculations for diagnostics.
/// Demonstrates: [Decorator] — wraps IReservationPricingService with Order=1.
/// </summary>
[Decorator(Order = 1)]
public class LoggingReservationPricingService(
    IReservationPricingService inner,
    ILogger<LoggingReservationPricingService> logger) : IReservationPricingService
{
    public async Task<decimal> CalculateTotalAsync(
        Guid roomTypeId,
        DateTimeOffset checkIn,
        DateTimeOffset checkOut,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Calculating pricing for RoomType {RoomTypeId}: {CheckIn} → {CheckOut}",
            roomTypeId, checkIn, checkOut);

        var total = await inner.CalculateTotalAsync(roomTypeId, checkIn, checkOut, ct).ConfigureAwait(false);

        logger.LogInformation(
            "Pricing result for RoomType {RoomTypeId}: {Total}",
            roomTypeId, total);

        return total;
    }
}
