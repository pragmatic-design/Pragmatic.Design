namespace Showcase.Booking.Reservations.Endpoints;

/// <summary>
/// Accepts guest feedback from a browser form.
/// Demonstrates: [RequireAntiforgery] on a form endpoint — token validation stays ON
/// (form endpoints otherwise get DisableAntiforgery()).
/// </summary>
[Endpoint(HttpVerb.Post, "/api/feedback-form")]
[RequireAntiforgery]
[ApiSummary("Submit feedback form")]
[ApiTags("Reservations")]
public partial class SubmitFeedbackFormEndpoint : Endpoint<string>
{
    [FromForm]
    public string Message { get; set; } = "";

    public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
        => Task.FromResult(Result<string>.Success(Message));
}
