using Showcase.Booking.Infrastructure.Processors;
using Showcase.Booking.Reservations.Endpoints;

namespace Showcase.Booking.Reservations.Actions;

/// <summary>
/// Retrieves a reservation by ID.
/// Demonstrates: [EndpointGroup] routing + [PreProcessor] pipeline, and [LoadEntity(Include = …)].
/// </summary>
/// <remarks>
///     The guest and the property are loaded with the row, because <see cref="ReservationSummaryDto" /> flattens their names. Declared rather
///     than written on <c>Query()</c>: an <c>Include</c> chain is EF Core's surface, and the declared load is what the Sql engine compiles too.
///     A reservation the caller cannot see is the load's 404.
/// </remarks>
[DomainAction]
[Endpoint(HttpVerb.Get, "/{id}")]
[EndpointGroup<ReservationsGroup>]
[RequirePermission("booking.reservation.read")]
[ApiSummary("Get Reservation")]
[ApiDescription("Retrieves a reservation by its unique identifier.")]
[ApiTags("Reservations")]
[PreProcessor<RequestLoggingPreProcessor>]
[LoadEntity<Reservation>(nameof(Id), Include = "Guest, Property")]
public partial class GetReservationAction : DomainAction<ReservationSummaryDto, NotFoundError>
{
    [FromRoute]
    public required Guid Id { get; init; }

    // Uses source-generated FromEntity() from [MapFrom<Reservation>]
    public override Task<Result<ReservationSummaryDto, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<ReservationSummaryDto, IError>>(ReservationSummaryDto.FromEntity(_reservation));
}
