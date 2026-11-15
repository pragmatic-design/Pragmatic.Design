using System.Net;

namespace Pragmatic.Notifications.Webhook;

/// <summary>
///     A delivery attempt the receiver answered with a non-success status: thrown inside the resilience
///     pipeline so its retry sees the attempt as failed, and turned back into a failed delivery after it.
/// </summary>
internal sealed class WebhookStatusException(HttpStatusCode status)
    : Exception($"HTTP {(int)status}")
{
    /// <summary>The status the receiver answered.</summary>
    public HttpStatusCode Status { get; } = status;
}
