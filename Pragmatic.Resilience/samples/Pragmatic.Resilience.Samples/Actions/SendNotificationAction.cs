using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Resilience.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Resilience.Samples.Actions;

/// <summary>
///     Simulates sending a notification (fire-and-forget style).
///     [ResiliencePolicy("notifications")] wraps with fast timeout.
///     VoidDomainAction — no return value.
/// </summary>
[DomainAction]
[ResiliencePolicy("notifications")]
public partial class SendNotificationAction : VoidDomainAction
{
    public required string Message { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        // Simulate notification send
        await Task.Delay(30, ct).ConfigureAwait(false);
        Console.WriteLine($"    [notification sent] {Message}");
        return Success;
    }
}
