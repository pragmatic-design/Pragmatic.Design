using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Notifications.Testing;

/// <summary>
///     DI registration for the notification test harness.
/// </summary>
public static class NotificationTestExtensions
{
    /// <summary>
    ///     Replaces <see cref="INotificationService"/> with <see cref="NotificationTestHarness"/>.
    ///     Returns the harness instance for assertions.
    /// </summary>
    public static NotificationTestHarness AddNotificationTestHarness(this IServiceCollection services)
    {
        var harness = new NotificationTestHarness();
        services.RemoveAll<INotificationService>();
        // Register the concrete harness once, then forward the interface to the same instance.
        services.AddSingleton(harness);
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationTestHarness>());
        return harness;
    }
}
