using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Http;
using Pragmatic.Notifications.Channels;

namespace Pragmatic.Notifications.Webhook;

/// <summary>
///     Webhook delivery channel. Sends notification content as HTTP POST to the recipient's webhook URL.
/// </summary>
internal sealed partial class WebhookChannel(
    IHttpClientFactory httpClientFactory,
    IOptions<WebhookOptions> options,
    ILogger<WebhookChannel> logger)
    : INotificationChannel
{
    private readonly WebhookOptions _options = options.Value;

    public NotificationChannel Channel => NotificationChannel.Webhook;

    public async Task<DeliveryResult> DeliverAsync(
        ResolvedRecipient recipient,
        NotificationContent content,
        CancellationToken ct = default)
    {
        // SSRF guard. The checks live in OutboundUrlGuard, not here: a webhook address is exactly the
        // "URL supplied by a user or a tenant" case the guard is written for, and a second private copy
        // is a copy that drifts — the two differed on 0.0.0.0/8, ::, CGNAT, multicast and embedded
        // credentials, every one of them a gap on this side.
        // allowHttp: true keeps this channel's existing acceptance of plain-http endpoints.
        if (!Uri.TryCreate(recipient.Address, UriKind.Absolute, out var uri))
        {
            LogWebhookBlocked(recipient.Address);
            return DeliveryResult.Failed(Describe(OutboundUrlVerdict.NotAnAbsoluteUrl));
        }

        // Scheme, embedded credentials, and a literal internal address — no DNS yet.
        var verdict = OutboundUrlGuard.Inspect(uri, allowHttp: true);

        // AllowPrivateNetworks waives the address check only. Scheme and credentials still apply:
        // the option is about trusting the network, not about accepting any URL at all.
        if (verdict == OutboundUrlVerdict.ResolvesToInternalAddress && _options.AllowPrivateNetworks)
            verdict = OutboundUrlVerdict.Allowed;

        if (verdict != OutboundUrlVerdict.Allowed)
        {
            LogWebhookBlocked(recipient.Address);
            return DeliveryResult.Failed(Describe(verdict));
        }

        // ...match the allowlist when one is configured...
        if (_options.AllowedHosts.Count > 0
            && !_options.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            LogWebhookBlocked(recipient.Address);
            return DeliveryResult.Failed("Webhook URL is not in the allowed hosts list.");
        }

        // ...and the host must not resolve to an internal address, which would let an attacker pivot
        // to internal services or the cloud metadata endpoint. Resolution failure is also a refusal.
        if (!_options.AllowPrivateNetworks)
        {
            var resolved = await OutboundUrlGuard
                .InspectResolvedAsync(uri, allowHttp: true, ct)
                .ConfigureAwait(false);

            if (resolved != OutboundUrlVerdict.Allowed)
            {
                LogWebhookBlocked(recipient.Address);
                return DeliveryResult.Failed(Describe(resolved));
            }
        }

        try
        {
            var client = httpClientFactory.CreateClient("Pragmatic.Notifications.Webhook");

            string json;
            if (content.JsonPayload is not null)
            {
                json = content.JsonPayload;
            }
            else
            {
                json = JsonSerializer.Serialize(new WebhookPayload
                {
                    Subject = content.Subject,
                    Body = content.Body,
                    Timestamp = DateTimeOffset.UtcNow,
                }, WebhookJsonContext.Default.WebhookPayload);
            }

            using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = httpContent };

            // Sign the payload (HMAC-SHA256 over "{timestamp}.{body}") so receivers can authenticate
            // it and reject replays.
            if (!string.IsNullOrEmpty(_options.SigningSecret))
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningSecret));
                var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{json}")));
                request.Headers.TryAddWithoutValidation("X-Pragmatic-Timestamp", timestamp);
                request.Headers.TryAddWithoutValidation("X-Pragmatic-Signature", $"sha256={signature.ToLowerInvariant()}");
            }

            var response = await client.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                LogWebhookSent(recipient.Address);
                return DeliveryResult.Succeeded();
            }

            // Do not include raw response body in DeliveryResult to avoid exposing sensitive remote data.
            LogWebhookFailed(recipient.Address, (int)response.StatusCode);
            return DeliveryResult.Failed($"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            LogWebhookException(recipient.Address, ex);
            return DeliveryResult.Failed(ex.Message);
        }
    }

    /// <summary>
    ///     Turns a guard verdict into the failure text handed back to the caller. Deliberately vague
    ///     about which internal address was reached — the detail belongs in the log, not in a message
    ///     that may travel back to whoever supplied the URL.
    /// </summary>
    private static string Describe(OutboundUrlVerdict verdict) => verdict switch
    {
        OutboundUrlVerdict.NotAnAbsoluteUrl or OutboundUrlVerdict.SchemeNotAllowed
            => "Webhook URL must be an absolute http(s) URL.",
        OutboundUrlVerdict.CredentialsInUrl
            => "Webhook URL must not embed credentials.",
        OutboundUrlVerdict.ResolvesToInternalAddress
            => "Webhook URL resolves to a private or loopback address.",
        OutboundUrlVerdict.HostCouldNotBeResolved
            => "Webhook URL host could not be resolved.",
        _ => "Webhook URL failed outbound validation.",
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook delivery blocked for URL '{Url}': failed SSRF/allowlist validation.")]
    private partial void LogWebhookBlocked(string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook delivered to {Url}")]
    private partial void LogWebhookSent(string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook delivery failed to {Url}: HTTP {StatusCode}")]
    private partial void LogWebhookFailed(string url, int statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook delivery exception to {Url}")]
    private partial void LogWebhookException(string url, Exception ex);
}
