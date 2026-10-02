using Showcase.Booking.Guests.Endpoints;

namespace Showcase.Booking.Guests.Queries;

/// <summary>
/// Retrieves a single guest by ID.
/// Demonstrates: a single-row read declared, not written — <c>Single = true</c> answers 404 when
/// nothing matches, inside the <see cref="GuestsGroup" /> route group.
/// </summary>
[Query<Guest, GuestDto>(Single = true)]
[Endpoint(HttpVerb.Get, "/{id}")]
[EndpointGroup<GuestsGroup>]
[RequirePermission("booking.guest.read")]
[ApiSummary("Get Guest")]
[ApiDescription("Retrieves a guest by their unique identifier.")]
[ApiTags("Guests")]
public partial class GetGuestQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
