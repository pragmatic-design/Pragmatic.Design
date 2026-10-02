using Microsoft.EntityFrameworkCore;
using Showcase.Booking.Infrastructure.Processors;

namespace Showcase.Booking.Reservations.Endpoints;

/// <summary>
/// Retrieves a reservation by ID.
/// Demonstrates: [EndpointGroup] routing + [PreProcessor] pipeline.
/// </summary>
[Endpoint(HttpVerb.Get, "/{id}")]
[EndpointGroup<ReservationsGroup>]
[RequirePermission("booking.reservation.read")]
[ApiSummary("Get Reservation")]
[ApiDescription("Retrieves a reservation by its unique identifier.")]
[ApiTags("Reservations")]
[PreProcessor<RequestLoggingPreProcessor>]
public partial class GetReservationEndpoint : Endpoint<ReservationSummaryDto>
{
    private IReadRepository<Reservation> _reservations = null!;

    [FromRoute]
    public Guid Id { get; set; }

    public override async Task<Result<ReservationSummaryDto>> HandleAsync(CancellationToken ct = default)
    {
        // Eagerly load Guest and Property — required by ReservationSummaryDto.FromEntity()
        var reservation = await _reservations.Query()
            .Include(r => r.Guest)
            .Include(r => r.Property)
            .FirstOrDefaultAsync(r => r.PersistenceId == Id, ct)
            .ConfigureAwait(false);

        if (reservation is null)
        {
            return Result<ReservationSummaryDto>.Failure(NotFoundError.For<Guid>("Reservation", Id));
        }

        // Uses source-generated FromEntity() from [MapFrom<Reservation>]
        return ReservationSummaryDto.FromEntity(reservation);
    }
}
