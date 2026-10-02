using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Extensions;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Configuration;

namespace Pragmatic.Notifications.Email;

/// <summary>
///     Builder extensions for adding the SMTP email notification channel.
///     Configures both the notification sender identity and the underlying Pragmatic.Email transport.
/// </summary>
public static class SmtpBuilderExtensions
{
    extension(NotificationsBuilder builder)
    {
        /// <summary>
        ///     Adds SMTP email channel with full transport configuration.
        /// </summary>
        public NotificationsBuilder AddSmtp(Action<SmtpOptions> configureSender,
            Action<SmtpTransportOptions> configureTransport)
        {
            builder.Services.Configure(configureSender);
            builder.Services.AddSingleton<INotificationChannel, SmtpChannel>();
            builder.Services.AddPragmaticEmail(email => email.UseSmtp(configureTransport));
            return builder;
        }
    }
}
