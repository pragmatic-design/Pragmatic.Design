using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Email;
using Pragmatic.Email.Builder;
using Pragmatic.Notifications.Channels;

namespace Pragmatic.Notifications.Email;

/// <summary>
///     Email notification channel. Delegates to <see cref="IEmailSender"/> from Pragmatic.Email
///     for SMTP delivery, connection pooling, DKIM, middleware pipeline, etc.
/// </summary>
/// <remarks>
///     The <c>From</c> is the recipient's tenant's, when an <see cref="ITenantEmailSenderResolver" /> is
///     registered and names one, and the options' otherwise. The tenant is read off the recipient, which
///     carries the one the notification was sent under: the channel factory is handed the same value.
/// </remarks>
internal sealed partial class SmtpChannel(
    IEmailSender emailSender,
    IOptions<SmtpOptions> options,
    ILogger<SmtpChannel> logger,
    ITenantEmailSenderResolver? senders = null)
    : INotificationChannel
{
    private readonly SmtpOptions _options = options.Value;

    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task<DeliveryResult> DeliverAsync(
        ResolvedRecipient recipient,
        NotificationContent content,
        CancellationToken ct = default)
    {
        try
        {
            var sender = await SenderForAsync(recipient.TenantId, ct).ConfigureAwait(false);
            var builder = new EmailMessageBuilder()
                .From(sender.Address, sender.Name)
                .To(recipient.Address)
                .Subject(content.Subject);

            if (content.Body is not null)
                builder.TextBody(content.Body);

            if (content.HtmlBody is not null)
                builder.HtmlBody(content.HtmlBody);

            var message = builder.Build();
            var result = await emailSender.SendAsync(message, ct).ConfigureAwait(false);

            if (result.Success)
            {
                LogEmailSent(recipient.Address);
                return DeliveryResult.Succeeded(result.MessageId);
            }

            LogEmailFailed(recipient.Address, result.ErrorMessage);
            return DeliveryResult.Failed(result.ErrorMessage ?? "Unknown email error");
        }
        catch (Exception ex)
        {
            LogEmailException(recipient.Address, ex);
            return DeliveryResult.Failed(ex.Message);
        }
    }

    private async ValueTask<EmailSenderIdentity> SenderForAsync(string? tenantId, CancellationToken ct)
    {
        if (senders is not null && !string.IsNullOrEmpty(tenantId)
            && await senders.ResolveAsync(tenantId, ct).ConfigureAwait(false) is { } declared)
            return declared;

        return new EmailSenderIdentity(_options.SenderAddress, _options.SenderName);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification email sent to {Address}")]
    private partial void LogEmailSent(string address);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification email failed to {Address}: {Error}")]
    private partial void LogEmailFailed(string address, string? error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification email exception to {Address}")]
    private partial void LogEmailException(string address, Exception ex);
}
