namespace Pragmatic.Notifications.Slack;

/// <summary>
///     Options for the Slack notification channel.
/// </summary>
public sealed class SlackOptions
{
    /// <summary>
    ///     Incoming-webhook URL used when the resolved recipient does not carry one of its own.
    /// </summary>
    /// <remarks>
    ///     Treat it as a secret: anyone holding the URL can post to the channel. Load it from user
    ///     secrets, environment variables or a secrets manager — never from committed configuration.
    /// </remarks>
    public string? DefaultWebhookUrl { get; set; }

    /// <summary>
    ///     Hosts accepted as Slack webhook targets (default: <c>hooks.slack.com</c>).
    /// </summary>
    /// <remarks>
    ///     A recipient address reaches this channel from application data, so it is untrusted input.
    ///     Restricting the host keeps a crafted address from turning notification delivery into a
    ///     server-side request forgery against an arbitrary endpoint. Widen it only for a self-hosted
    ///     Slack-compatible receiver you control.
    /// </remarks>
    public IReadOnlyList<string> AllowedHosts { get; set; } = ["hooks.slack.com"];

    /// <summary>Request timeout for a single delivery (default: 30 seconds).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
