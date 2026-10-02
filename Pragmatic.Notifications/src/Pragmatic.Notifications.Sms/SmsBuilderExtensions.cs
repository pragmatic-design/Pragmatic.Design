using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Configuration;

namespace Pragmatic.Notifications.Sms;

/// <summary>
///     Builder extension for adding the SMS channel.
/// </summary>
public static class SmsBuilderExtensions
{
    /// <summary>Adds the Twilio-backed SMS delivery channel.</summary>
    public static NotificationsBuilder AddTwilioSms(
        this NotificationsBuilder builder,
        Action<TwilioSmsOptions> configure)
    {
        builder.Services.Configure(configure);

        builder.Services.AddHttpClient(TwilioSmsChannel.HttpClientName, client =>
        {
            // A stalled provider must not hold the delivery pipeline open indefinitely.
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        builder.Services.AddSingleton<INotificationChannel, TwilioSmsChannel>();
        return builder;
    }
}
