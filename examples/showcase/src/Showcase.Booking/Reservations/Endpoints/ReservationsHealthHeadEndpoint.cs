namespace Showcase.Booking.Reservations.Endpoints;

/// <summary>
/// Availability probe for the reservations feature.
/// Demonstrates: HttpVerb.Head (mapped via MapMethods, 204 with no body).
/// </summary>
[Endpoint(HttpVerb.Head, "/api/reservations-availability")]
[ApiSummary("Reservations availability probe")]
[ApiTags("Reservations")]
public partial class ReservationsHealthHeadEndpoint : VoidEndpoint
{
    public override Task<VoidResult> HandleAsync(CancellationToken ct = default)
        => Task.FromResult(VoidResult.Success());
}
