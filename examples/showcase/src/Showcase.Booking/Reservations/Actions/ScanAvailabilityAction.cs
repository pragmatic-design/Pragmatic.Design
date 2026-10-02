using System.Runtime.CompilerServices;

namespace Showcase.Booking.Reservations.Actions;

/// <summary>
/// Streams availability slots as they are scanned.
/// Demonstrates: StreamingDomainAction (SSE) — the action invoker pipeline (validation,
/// authorization filters) runs BEFORE the stream opens; items stream incrementally.
/// </summary>
[DomainAction]
[Endpoint(HttpVerb.Get, "/api/availability-scan")]
[ApiSummary("Scan availability")]
[ApiTags("Reservations")]
public partial class ScanAvailabilityAction : StreamingDomainAction<string>
{
    public override async IAsyncEnumerable<Result<string, IError>> ExecuteStream(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var hour = 9; hour <= 11; hour++)
        {
            ct.ThrowIfCancellationRequested();
            yield return Result<string, IError>.Success($"slot-{hour}:00");
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
    }
}
