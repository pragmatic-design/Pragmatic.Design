using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Configuration;

namespace Pragmatic.Notifications.Slack;

/// <summary>
///     Builder extension for adding the Slack channel.
/// </summary>
public static class SlackBuilderExtensions
{
    /// <summary>Adds the Slack delivery channel (incoming webhook).</summary>
    public static NotificationsBuilder AddSlack(
        this NotificationsBuilder builder,
        Action<SlackOptions>? configure = null)
    {
        if (configure is not null)
            builder.Services.Configure(configure);

        builder.Services.AddHttpClient(SlackChannel.HttpClientName, client =>
            {
                // A hung Slack endpoint must not stall the delivery pipeline.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // An allowed host must stay allowed: a 30x could otherwise bounce the request elsewhere.
                AllowAutoRedirect = false,
            });

        builder.Services.AddSingleton<INotificationChannel, SlackChannel>();
        return builder;
    }
}
