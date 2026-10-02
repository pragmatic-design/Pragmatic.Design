using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Configuration;

namespace Pragmatic.Notifications.Webhook;

/// <summary>
///     Builder extensions for adding webhook channel.
/// </summary>
public static class WebhookBuilderExtensions
{
    /// <summary>Adds the webhook delivery channel (HTTP POST).</summary>
    public static NotificationsBuilder AddWebhook(this NotificationsBuilder builder, Action<WebhookOptions>? configure = null)
    {
        if (configure is not null)
            builder.Services.Configure(configure);

        builder.Services.AddHttpClient("Pragmatic.Notifications.Webhook", client =>
            {
                // Prevent a slow/hung webhook from blocking the delivery pipeline indefinitely.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // SSRF hardening: disable auto-redirects so an allowlisted/validated URL cannot be
                // bounced to an internal target via a 30x response.
                AllowAutoRedirect = false,
            });
        builder.Services.AddSingleton<INotificationChannel, WebhookChannel>();
        return builder;
    }
}
