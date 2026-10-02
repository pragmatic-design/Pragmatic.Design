using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Notifications.Channels;

namespace Pragmatic.Notifications.Sms;

/// <summary>
///     SMS delivery channel backed by the Twilio REST API, called over plain HTTP so the library keeps
///     no vendor SDK dependency.
/// </summary>
internal sealed partial class TwilioSmsChannel(
    IHttpClientFactory httpClientFactory,
    IOptions<TwilioSmsOptions> options,
    ILogger<TwilioSmsChannel> logger)
    : INotificationChannel
{
    internal const string HttpClientName = "Pragmatic.Notifications.Sms";

    /// <summary>A single GSM-7 segment; longer texts are split and billed per segment by the carrier.</summary>
    private const int SingleSegmentLength = 160;

    private readonly TwilioSmsOptions _options = options.Value;

    public NotificationChannel Channel => NotificationChannel.Sms;

    public async Task<DeliveryResult> DeliverAsync(
        ResolvedRecipient recipient,
        NotificationContent content,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recipient.Address))
            return DeliveryResult.Failed("SMS delivery requires a phone number as the recipient address.");

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(_options.BaseAddress, $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json"));

            // Basic auth over TLS, exactly as Twilio's API expects.
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var text = Compose(content);

            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = recipient.Address,
                ["From"] = _options.FromNumber,
                ["Body"] = text,
            });

            var response = await client.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                // Segments are the billing unit, so they are worth having in the log: a body that
                // quietly grew past 160 characters costs twice as much to send.
                LogSent(Redact(recipient.Address), SegmentCount(text));
                return DeliveryResult.Succeeded();
            }

            // The response body echoes the request parameters, phone numbers included, so only the
            // status code is surfaced and logged.
            LogFailed(Redact(recipient.Address), (int)response.StatusCode);
            return DeliveryResult.Failed($"Twilio rejected the message with HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            LogException(Redact(recipient.Address), ex);
            return DeliveryResult.Failed(ex.Message);
        }
    }

    /// <summary>
    ///     Picks the text to send: <see cref="NotificationContent.ShortBody"/> when provided, otherwise
    ///     the plain body. Subject and HTML are ignored — an SMS has neither.
    /// </summary>
    private static string Compose(NotificationContent content)
    {
        if (!string.IsNullOrWhiteSpace(content.ShortBody))
            return content.ShortBody;

        return string.IsNullOrWhiteSpace(content.Subject)
            ? content.Body
            : $"{content.Subject}: {content.Body}";
    }

    /// <summary>
    ///     Masks all but the last two digits of a phone number for logging: the destination of a message
    ///     is personal data and does not belong in logs in full.
    /// </summary>
    private static string Redact(string phoneNumber)
        => phoneNumber.Length <= 2
            ? "***"
            : string.Concat("***", phoneNumber.AsSpan(phoneNumber.Length - 2));

    [LoggerMessage(Level = LogLevel.Information, Message = "SMS delivered to {Recipient} ({Segments} segment(s))")]
    private partial void LogSent(string recipient, int segments);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMS delivery failed to {Recipient}: HTTP {StatusCode}")]
    private partial void LogFailed(string recipient, int statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "SMS delivery exception to {Recipient}")]
    private partial void LogException(string recipient, Exception ex);

    /// <summary>Number of billable segments a body of the given length occupies.</summary>
    internal static int SegmentCount(string body)
        => body.Length == 0
            ? 0
            : (int)Math.Ceiling(body.Length / (double)SingleSegmentLength);
}
