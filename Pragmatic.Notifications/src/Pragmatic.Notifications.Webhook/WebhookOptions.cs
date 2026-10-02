namespace Pragmatic.Notifications.Webhook;

/// <summary>
///     Options for the webhook notification channel.
/// </summary>
public sealed class WebhookOptions
{
    /// <summary>
    ///     Allowlist of permitted webhook hosts (e.g., "hooks.example.com").
    ///     When non-empty, only URLs whose host matches an entry in this list are delivered.
    ///     Leave empty to allow all hosts (not recommended for production).
    /// </summary>
    public IReadOnlyList<string> AllowedHosts { get; set; } = [];

    /// <summary>
    ///     When false (default), webhook URLs that resolve to a loopback, link-local or private
    ///     (RFC 1918 / unique-local) address are blocked — an SSRF guard against pivoting to internal
    ///     services or cloud metadata endpoints (169.254.169.254). Set true only in trusted networks.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>
    ///     When set, each delivery is signed: an <c>X-Pragmatic-Signature</c> header carries
    ///     <c>sha256=&lt;hex&gt;</c> of <c>HMAC-SHA256(secret, "{timestamp}.{body}")</c> and
    ///     <c>X-Pragmatic-Timestamp</c> carries the Unix-seconds timestamp, so receivers can verify
    ///     authenticity and reject replays. Null = unsigned.
    /// </summary>
    public string? SigningSecret { get; set; }
}
