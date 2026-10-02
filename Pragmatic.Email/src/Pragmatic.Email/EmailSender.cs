using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Pragmatic.Email.Diagnostics;
using Pragmatic.Email.Middleware;
using Pragmatic.Email.Transport;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Email;

/// <summary>
///     Default email sender. Runs the middleware pipeline then delegates to the transport.
/// </summary>
internal sealed partial class EmailSender(
    IEmailTransport transport,
    EmailPipeline pipeline,
    ILogger<EmailSender> logger)
    : IEmailSender
{
    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        // Contract: middleware may not replace Subject with null; Subject is required string on EmailMessage.
        var processed = await pipeline.ExecuteAsync(message, ct).ConfigureAwait(false);

        // Checked after the pipeline, because DefaultFromMiddleware may have supplied it. Failing here
        // with an actionable message beats letting the transport emit "From: " and having the server
        // reject the message with an opaque protocol error.
        if (string.IsNullOrWhiteSpace(processed.From.Address))
        {
            LogMissingFrom(processed.MessageId);
            return EmailResult.Failed(
                "The message has no From address. Set it on the message (EmailMessageBuilder.From) "
                + "or configure a fallback via EmailOptions.DefaultFrom.",
                processed.MessageId);
        }

        LogSending(processed.Subject, processed.To.Count);

        using var activity = EmailDiagnostics.ActivitySource.StartActivity("Email.Send", ActivityKind.Producer);
        activity?.SetTag(EmailTags.Transport, transport.Name);
        activity?.SetTag(EmailTags.Recipients, processed.To.Count);

        var timestamp = Stopwatch.GetTimestamp();
        var result = await transport.SendAsync(processed, ct).ConfigureAwait(false);
        var elapsedMs = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

        var transportTag = new KeyValuePair<string, object?>("transport", transport.Name);
        EmailDiagnostics.SendDuration.Record(elapsedMs, transportTag);

        if (result.Success)
        {
            EmailDiagnostics.EmailsSent.Add(1, transportTag);
            activity?.SetStatus(ActivityStatusCode.Ok);
            LogSent(processed.MessageId);
        }
        else
        {
            EmailDiagnostics.EmailsFailed.Add(1, transportTag);
            activity?.SetStatus(ActivityStatusCode.Error, result.ErrorMessage);
            LogFailed(processed.MessageId, result.ErrorMessage);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sending email: {Subject} to {RecipientCount} recipient(s)")]
    private partial void LogSending(string subject, int recipientCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email sent: {MessageId}")]
    private partial void LogSent(string messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email failed: {MessageId} — {Error}")]
    private partial void LogFailed(string messageId, string? error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email not sent: {MessageId} has no From address and no EmailOptions.DefaultFrom is configured")]
    private partial void LogMissingFrom(string messageId);
}
