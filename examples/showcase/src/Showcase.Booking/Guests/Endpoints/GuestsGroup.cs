namespace Showcase.Booking.Guests.Endpoints;

/// <summary>
/// Endpoint group for guest management.
/// </summary>
[EndpointGroup("/api/guests", Tag = "Guests")]
[ApiVersion("1.0")]
public sealed class GuestsGroup;
