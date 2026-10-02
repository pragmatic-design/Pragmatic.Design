namespace Showcase.Booking.Reservations.Endpoints;

/// <summary>
/// Issues a booking token.
/// Demonstrates: [Idempotent] — retries with the same Idempotency-Key replay the original
/// response without re-executing the handler (the execution counter makes replay observable).
/// </summary>
[Endpoint(HttpVerb.Post, "/api/booking-tokens")]
[Idempotent(DurationSeconds = 300)]
[ApiSummary("Issue booking token")]
[ApiTags("Reservations")]
public partial class CreateBookingTokenEndpoint : Endpoint<string>
{
    private static int s_executions;

    public required string Purpose { get; set; }

    public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
    {
        var execution = Interlocked.Increment(ref s_executions);
        return Task.FromResult(Result<string>.Success($"{Purpose}-token-{execution}"));
    }
}
