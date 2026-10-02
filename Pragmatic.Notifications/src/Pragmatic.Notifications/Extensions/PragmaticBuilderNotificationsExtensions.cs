using Pragmatic.Composition;
using Pragmatic.Notifications.Configuration;
using Pragmatic.Notifications.Extensions;

namespace Pragmatic.Notifications;

/// <summary>
///     Extension methods for configuring Notifications on <see cref="IPragmaticBuilder"/>.
/// </summary>
public static class PragmaticBuilderNotificationsExtensions
{
    /// <summary>
    ///     Configures the notification system for the application.
    /// </summary>
    public static IPragmaticBuilder UseNotifications(
        this IPragmaticBuilder builder,
        Action<NotificationsBuilder>? configure = null)
    {
        builder.Services.AddPragmaticNotifications(configure ?? (_ => { }));
        return builder;
    }
}
