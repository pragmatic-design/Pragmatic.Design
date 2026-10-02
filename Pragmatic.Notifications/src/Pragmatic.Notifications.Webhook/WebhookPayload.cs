namespace Pragmatic.Notifications.Webhook;

/// <summary>
///     Default JSON payload sent to a webhook endpoint.
/// </summary>
internal sealed class WebhookPayload
{
    public string Subject { get; init; } = null!;
    public string Body { get; init; } = null!;
    public DateTimeOffset Timestamp { get; init; }
}
