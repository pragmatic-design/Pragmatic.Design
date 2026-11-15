using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Http;
using Pragmatic.Notifications.Channels;
using Pragmatic.Resilience;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Notifications.Webhook;

/// <summary>
///     Webhook delivery channel. Sends notification content as HTTP POST to the recipient's webhook URL.
/// </summary>
/// <remarks>
///     A delivery has one id and one secret (the destination's, from <see cref="IWebhookSigningSecretResolver" />,
///     or the application's), and runs through the <see cref="WebhookOptions.ResiliencePolicy" /> when one is
///     named: every attempt carries the same id.
/// </remarks>
internal sealed partial class WebhookChannel(
    IHttpClientFactory httpClientFactory,
    IOptions<WebhookOptions> options,
    ILogger<WebhookChannel> logger,
    IWebhookSigningSecretResolver? secrets = null,
    IResiliencePipelineProvider? pipelines = null)
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

            // One id and one secret for the delivery, whatever the number of attempts: a receiver that saw
            // the first one recognises the second by the id, and verifies both with the same secret.
            var deliveryId = Guid.NewGuid().ToString("N");
            var secret = await SecretForAsync(uri, recipient.TenantId, ct).ConfigureAwait(false);

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

            var pipeline = _options.ResiliencePolicy is { Length: > 0 } policy && pipelines is not null
                ? pipelines.GetPipeline(policy)
                : null;

            if (pipeline is null)
                await AttemptAsync(client, uri, json, deliveryId, secret, ct).ConfigureAwait(false);
            else
                await pipeline.ExecuteAsync(
                    (_, attemptCt) => AttemptAsync(client, uri, json, deliveryId, secret, attemptCt),
                    new ResilienceContext { OperationName = "Pragmatic.Notifications.Webhook", OperationKey = uri.Host },
                    ct).ConfigureAwait(false);

            LogWebhookSent(recipient.Address);
            return DeliveryResult.Succeeded();
        }
        catch (Exception ex) when (StatusOf(ex) is { } status)
        {
            // Do not include raw response body in DeliveryResult to avoid exposing sensitive remote data.
            LogWebhookFailed(recipient.Address, (int)status);
            return DeliveryResult.Failed($"HTTP {(int)status}");
        }
        catch (Exception ex)
        {
            LogWebhookException(recipient.Address, ex);
            return DeliveryResult.Failed(ex.Message);
        }
    }

    /// <summary>
    ///     One attempt: a fresh request — a sent one cannot be sent again — signed with a fresh timestamp
    ///     and the delivery's id, and a non-success status thrown so a retry sees it.
    /// </summary>
    private static async Task AttemptAsync(
        HttpClient client, Uri uri, string json, string deliveryId, string? secret, CancellationToken ct)
    {
        using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = httpContent };
        request.Headers.TryAddWithoutValidation("X-Pragmatic-Delivery", deliveryId);

        // Sign the payload (HMAC-SHA256 over "{timestamp}.{deliveryId}.{body}") so receivers can
        // authenticate it, reject replays, and trust the id they de-duplicate on.
        if (!string.IsNullOrEmpty(secret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{deliveryId}.{json}")));
            request.Headers.TryAddWithoutValidation("X-Pragmatic-Timestamp", timestamp);
            request.Headers.TryAddWithoutValidation("X-Pragmatic-Signature", $"sha256={signature.ToLowerInvariant()}");
        }

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new WebhookStatusException(response.StatusCode);
    }

    private async ValueTask<string?> SecretForAsync(Uri destination, string? tenantId, CancellationToken ct)
        => secrets is not null && await secrets.ResolveAsync(destination, tenantId, ct).ConfigureAwait(false) is { Length: > 0 } own
            ? own
            : _options.SigningSecret;

    /// <summary>The status a refused delivery was answered with, through the retry's wrapping when there was one.</summary>
    private static System.Net.HttpStatusCode? StatusOf(Exception ex) => ex switch
    {
        WebhookStatusException refused => refused.Status,
        RetryExhaustedException { InnerException: WebhookStatusException refused } => refused.Status,
        _ => null
    };

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
