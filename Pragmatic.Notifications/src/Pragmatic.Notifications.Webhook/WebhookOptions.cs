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
    ///     <c>sha256=&lt;hex&gt;</c> of <c>HMAC-SHA256(secret, "{timestamp}.{deliveryId}.{body}")</c>,
    ///     <c>X-Pragmatic-Timestamp</c> carries the Unix-seconds timestamp and <c>X-Pragmatic-Delivery</c>
    ///     the delivery id, so receivers can verify authenticity, reject replays and recognise a
    ///     redelivery. Null = unsigned.
    /// </summary>
    /// <remarks>
    ///     The application's secret. A destination with a secret of its own gets it from an
    ///     <see cref="IWebhookSigningSecretResolver" />, and this one is what every other destination is
    ///     signed with.
    /// </remarks>
    public string? SigningSecret { get; set; }

    /// <summary>
    ///     The name of a <c>Pragmatic.Resilience</c> policy each delivery runs through — typically one with
    ///     a retry — or <see langword="null" /> for a single attempt.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An attempt that answers a non-success status or throws is retried as the policy says, with
    ///         the <b>same</b> delivery id, so a receiver that saw the first one can tell the second is not
    ///         a new event. The policy is registered with <c>AddResiliencePolicy(name, …)</c> beside
    ///         <c>AddPragmaticResilience()</c>; a name nobody registered runs a single attempt.
    ///     </para>
    ///     <para>
    ///         <see cref="Timeout" /> bounds each attempt, not the whole of them.
    ///     </para>
    /// </remarks>
    public string? ResiliencePolicy { get; set; }

    /// <summary>
    ///     How long one delivery may take, connection included, before it fails. Default 30 seconds.
    /// </summary>
    /// <remarks>
    ///     A slow or hung endpoint would otherwise hold the delivery pipeline. The failure is the
    ///     delivery's, recorded like any other: the notification is not retried here.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
