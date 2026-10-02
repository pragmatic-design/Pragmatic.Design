using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Mime;
using Pragmatic.Email.Transport;

namespace Pragmatic.Email.Smtp;

/// <summary>
///     SMTP transport with connection pooling, STARTTLS, AUTH (PLAIN/LOGIN/XOAUTH2), and full MIME support.
/// </summary>
internal sealed partial class SmtpTransport(IOptions<SmtpTransportOptions> options, ILogger<SmtpTransport> logger)
    : IEmailTransport
{
    private readonly SmtpConnectionPool _pool = new(options.Value);

    public string Name => "SMTP";

    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mimeBody = MimeWriter.Write(message);
        SmtpConnection? connection = null;

        try
        {
            connection = await _pool.AcquireAsync(ct).ConfigureAwait(false);
            LogConnectionAcquired(connection.MessagesSent);

            var result = await connection.SendMessageAsync(mimeBody, message, ct).ConfigureAwait(false);

            if (result.Success)
                LogEmailSent(message.MessageId, message.To.Count > 0 ? message.To[0].Address : string.Empty);
            else
                LogEmailFailed(message.MessageId, result.ErrorMessage);

            return result;
        }
        catch (Exception ex)
        {
            LogEmailException(message.MessageId, ex);

            // Connection is likely broken — don't return to pool
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                connection = null;
            }

            return EmailResult.Failed(ex.Message);
        }
        finally
        {
            if (connection is not null)
                await _pool.ReleaseAsync(connection).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _pool.DisposeAsync().ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "SMTP connection acquired (messages sent: {MessagesSent})")]
    private partial void LogConnectionAcquired(int messagesSent);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email sent via SMTP: {MessageId} to {Address}")]
    private partial void LogEmailSent(string messageId, string address);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email failed via SMTP: {MessageId} — {Error}")]
    private partial void LogEmailFailed(string messageId, string? error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Email exception via SMTP: {MessageId}")]
    private partial void LogEmailException(string messageId, Exception ex);
}
