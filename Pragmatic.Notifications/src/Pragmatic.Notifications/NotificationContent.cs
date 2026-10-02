namespace Pragmatic.Notifications;

/// <summary>
///     V1 notification content — developer provides strings directly.
///     V2 will add TemplateId + Data for server-side rendering via Pragmatic.Templates.
/// </summary>
public sealed record NotificationContent
{
    /// <summary>Notification subject/title.</summary>
    public required string Subject { get; init; }

    /// <summary>Plain text body (used for all channels as fallback).</summary>
    public required string Body { get; init; }

    /// <summary>HTML body for email channel.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>Short body for push/SMS (≤160 chars).</summary>
    public string? ShortBody { get; init; }

    /// <summary>JSON payload for webhook channel.</summary>
    public string? JsonPayload { get; init; }
}
