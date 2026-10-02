namespace Showcase.Booking.Reservations.Endpoints;

/// <summary>
/// Accepts a short booking note.
/// Demonstrates: [MaxBodySize] — requests over 256 bytes are rejected with 413
/// by RequestLimitsStep before the body is read.
/// </summary>
[Endpoint(HttpVerb.Post, "/api/booking-notes")]
[MaxBodySize(256)]
[ApiSummary("Create booking note")]
[ApiDescription("Stores a short note attached to the booking session.")]
[McpTool(Description = "Stores a short booking note (max 256-byte request).")]
[RequestExample("""{ "text": "short note" }""", Name = "minimal", Summary = "Minimal note")]
[RequestExample("""{ "text": "urgent: guest allergic to nuts" }""", Name = "urgent")]
[ResponseExample(201, "\"short note\"")]
[ApiTags("Reservations")]
public partial class CreateBookingNoteEndpoint : Endpoint<string>
{
    public required string Text { get; set; }

    public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
        => Task.FromResult(Result<string>.Success(Text));
}
