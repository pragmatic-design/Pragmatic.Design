namespace Showcase.Booking.Reservations.Endpoints;

/// <summary>
/// Echoes the booking session hint.
/// Demonstrates: [FromCookie] binding (optional cookie, bound in the handler body).
/// </summary>
[Endpoint(HttpVerb.Get, "/api/session-hint")]
[ApiSummary("Get session hint")]
[McpTool(Description = "Returns the current booking session hint.")]
[ApiTags("Reservations")]
public partial class GetSessionHintEndpoint : Endpoint<string>
{
    [FromCookie("session-hint", IsRequired = false)]
    public string? SessionHint { get; set; }

    public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
        => Task.FromResult(Result<string>.Success(SessionHint ?? "none"));
}
