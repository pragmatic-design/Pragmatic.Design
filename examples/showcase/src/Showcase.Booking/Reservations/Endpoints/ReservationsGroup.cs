namespace Showcase.Booking.Reservations.Endpoints;

/// <summary>
/// Endpoint group for reservation management.
/// Demonstrates: [EndpointGroup] with tag and API version.
/// </summary>
[EndpointGroup("/api/reservations", Tag = "Reservations")]
[ApiVersion("1.0")]
public sealed class ReservationsGroup;
