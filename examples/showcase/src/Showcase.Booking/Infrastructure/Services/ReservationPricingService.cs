using Showcase.Catalog.Entities;

namespace Showcase.Booking.Infrastructure.Services;

/// <summary>
/// Default pricing service: nights × base rate.
/// Demonstrates: [Service] — auto-registered as IReservationPricingService.
/// </summary>
[Service]
#pragma warning disable PRAG1641 // IReadRepository is registered by EF Core infrastructure
public class ReservationPricingService(IReadRepository<RoomType> roomTypes) : IReservationPricingService
#pragma warning restore PRAG1641
{
    public async Task<decimal> CalculateTotalAsync(
        Guid roomTypeId,
        DateTimeOffset checkIn,
        DateTimeOffset checkOut,
        CancellationToken ct = default)
    {
        var roomType = await roomTypes.GetByIdAsync(roomTypeId, ct).ConfigureAwait(false);
        if (roomType is null)
            return 0m;

        var nights = (int)(checkOut - checkIn).TotalDays;
        return nights * roomType.BaseRate;
    }
}
