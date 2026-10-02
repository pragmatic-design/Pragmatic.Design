using Showcase.Booking.Infrastructure.Processors;
using Showcase.Catalog.Entities;

namespace Showcase.Booking.Endpoints;

/// <summary>
/// Searches for available rooms at a property for a date range.
/// Demonstrates: [AllowAnonymous] — public search without auth.
///               [RateLimit] — throttle search requests.
///               [PostProcessor] — response audit logging.
/// </summary>
/// <remarks>
/// This is intentionally a manual <see cref="Endpoint{TResponse}"/> rather than a <c>[Query&lt;T,R&gt;]</c>.
/// Reason: availability calculation requires multiple round-trips (one CountAsync per RoomType)
/// and stateful per-item logic that cannot be expressed as a single IQueryable projection.
/// Use <c>[Query&lt;T,R&gt;]</c> for simple filtered list endpoints (see <c>SearchPropertiesQuery</c>).
/// </remarks>
[Endpoint(HttpVerb.Get, "/api/availability")]
[ApiSummary("Search Available Rooms")]
[ApiTags("Availability")]
[AllowAnonymous]
// ⚠️ A cache directive is only safe on a read whose answer does not depend on who is asking, and
// this is the one endpoint in the Showcase that qualifies: [AllowAnonymous], and the answer is a
// function of the query string alone. Location.Client keeps it out of shared caches all the same —
// a few seconds of reuse in the browser that is re-searching, and no proxy holding somebody's
// availability. The shared form (the default, Location.Any) generates a CacheOutput policy and
// needs app.UseOutputCache() in the host, which no example wires.
[ResponseCache(Duration = 15, Location = ResponseCacheLocation.Client)]
[RateLimit(Requests = 100, Window = "1m")]
[PostProcessor<ResponseAuditPostProcessor>]
public partial class SearchAvailableRoomsEndpoint : Endpoint<AvailableRoomResult[]>
{
    private IReadRepository<RoomType> _roomTypes = null!;
    private IReadRepository<Reservation> _reservations = null!;

    [FromQuery]
    public Guid PropertyId { get; set; }

    [FromQuery]
    public DateTimeOffset CheckIn { get; set; }

    [FromQuery]
    public DateTimeOffset CheckOut { get; set; }

    [FromQuery]
    public int Guests { get; set; } = 1;

    public override async Task<Result<AvailableRoomResult[]>> HandleAsync(CancellationToken ct = default)
    {
        var roomTypes = await _roomTypes
            .FindAsync(Spec<RoomType>.Where(rt => rt.PropertyId == PropertyId && rt.MaxOccupancy >= Guests), ct)
            .ConfigureAwait(false);

        var results = new List<AvailableRoomResult>();

        foreach (var roomType in roomTypes)
        {
            var overlapSpec = ReservationSpecifications.Overlapping(PropertyId, roomType.Id, CheckIn, CheckOut);
            var bookedCount = await _reservations.CountAsync(overlapSpec, ct).ConfigureAwait(false);
            var available = roomType.TotalRooms - bookedCount;

            if (available > 0)
            {
                results.Add(new AvailableRoomResult
                {
                    RoomTypeId = roomType.Id,
                    RoomTypeName = roomType.Name,
                    MaxOccupancy = roomType.MaxOccupancy,
                    BaseRate = roomType.BaseRate,
                    Currency = roomType.Currency,
                    AvailableRooms = (int)available
                });
            }
        }

        return results.ToArray();
    }
}
