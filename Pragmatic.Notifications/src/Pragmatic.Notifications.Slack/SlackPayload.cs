using System.Text.Json.Serialization;

namespace Pragmatic.Notifications.Slack;

/// <summary>
///     Payload posted to a Slack incoming webhook.
/// </summary>
internal sealed class SlackPayload
{
    /// <summary>Message text. Slack renders it with mrkdwn.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = null!;
}
