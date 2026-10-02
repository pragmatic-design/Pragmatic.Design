using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Notifications.Channels;

namespace Pragmatic.Notifications.Slack;

/// <summary>
///     Slack delivery channel: posts the notification to a Slack incoming webhook.
/// </summary>
/// <remarks>
///     The target URL is the resolved recipient's address when it carries one, otherwise
///     <see cref="SlackOptions.DefaultWebhookUrl"/> — which is the usual setup for alerting a single
///     ops channel.
/// </remarks>
internal sealed partial class SlackChannel(
    IHttpClientFactory httpClientFactory,
    IOptions<SlackOptions> options,
    ILogger<SlackChannel> logger)
    : INotificationChannel
{
    internal const string HttpClientName = "Pragmatic.Notifications.Slack";

    private readonly SlackOptions _options = options.Value;

    public NotificationChannel Channel => NotificationChannel.Slack;

    public async Task<DeliveryResult> DeliverAsync(
        ResolvedRecipient recipient,
        NotificationContent content,
        CancellationToken ct = default)
    {
        var target = !string.IsNullOrWhiteSpace(recipient.Address) && recipient.Address.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? recipient.Address
            : _options.DefaultWebhookUrl;

        if (string.IsNullOrWhiteSpace(target))
        {
            LogNoTarget();
            return DeliveryResult.Failed(
                "No Slack webhook URL: the recipient carries none and SlackOptions.DefaultWebhookUrl is not configured.");
        }

        if (!IsAllowedTarget(target, out var uri))
        {
            LogBlocked(target);
            return DeliveryResult.Failed(
                "Slack webhook URL rejected: it must be an https URL on an allowed host "
                + $"({string.Join(", ", _options.AllowedHosts)}).");
        }

        try
        {
            var payload = JsonSerializer.Serialize(
                new SlackPayload { Text = Compose(content) }, SlackJsonContext.Default.SlackPayload);

            using var body = new StringContent(payload, Encoding.UTF8, "application/json");
            var client = httpClientFactory.CreateClient(HttpClientName);

            var response = await client.PostAsync(uri, body, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                LogSent(uri.Host);
                return DeliveryResult.Succeeded();
            }

            // Slack answers with a short diagnostic ("invalid_payload", "channel_not_found") that is
            // safe and genuinely useful to surface; anything longer is truncated rather than logged whole.
            var detail = await ReadShortReasonAsync(response, ct).ConfigureAwait(false);
            LogFailed(uri.Host, (int)response.StatusCode, detail);
            return DeliveryResult.Failed($"HTTP {(int)response.StatusCode}: {detail}");
        }
        catch (Exception ex)
        {
            LogException(uri.Host, ex);
            return DeliveryResult.Failed(ex.Message);
        }
    }

    /// <summary>Builds the message text: subject as a bold first line, then the body.</summary>
    private static string Compose(NotificationContent content)
        => string.IsNullOrWhiteSpace(content.Subject)
            ? content.Body
            : $"*{content.Subject}*\n{content.Body}";

    private bool IsAllowedTarget(string target, out Uri uri)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
        {
            uri = null!;
            return false;
        }

        uri = parsed;
        return _options.AllowedHosts.Contains(parsed.Host, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadShortReasonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            text = text.Trim();
            return text.Length <= 100 ? text : text[..100];
        }
        catch (Exception)
        {
            return response.ReasonPhrase ?? "unknown";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Slack delivery skipped: no webhook URL configured")]
    private partial void LogNoTarget();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Slack delivery blocked for '{Url}': not an allowed https webhook host")]
    private partial void LogBlocked(string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Slack notification delivered to {Host}")]
    private partial void LogSent(string host);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Slack delivery failed to {Host}: HTTP {StatusCode} — {Detail}")]
    private partial void LogFailed(string host, int statusCode, string detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Slack delivery exception to {Host}")]
    private partial void LogException(string host, Exception ex);
}
