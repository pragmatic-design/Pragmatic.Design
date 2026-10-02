using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Preferences;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications.Configuration;

/// <summary>
///     Fluent builder for configuring the notification system.
/// </summary>
public sealed class NotificationsBuilder
{
    public IServiceCollection Services { get; }

    internal NotificationsBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>Registers a custom notification channel.</summary>
    public NotificationsBuilder AddChannel<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TChannel>()
        where TChannel : class, INotificationChannel
    {
        Services.AddSingleton<INotificationChannel, TChannel>();
        return this;
    }

    /// <summary>Registers a custom notification channel instance.</summary>
    public NotificationsBuilder AddChannel(INotificationChannel channel)
    {
        Services.AddSingleton<INotificationChannel>(channel);
        return this;
    }

    /// <summary>Uses in-memory notification store (default, development only).</summary>
    public NotificationsBuilder UseInMemoryStore()
    {
        Services.AddSingleton<INotificationStore, InMemoryNotificationStore>();
        return this;
    }

    /// <summary>Uses a custom notification store.</summary>
    public NotificationsBuilder UseStore<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>()
        where TStore : class, INotificationStore
    {
        Services.AddSingleton<INotificationStore, TStore>();
        return this;
    }

    /// <summary>Uses the default preference provider (all notifications enabled).</summary>
    public NotificationsBuilder UseDefaultPreferences()
    {
        Services.AddScoped<INotificationPreferenceProvider, DefaultPreferenceProvider>();
        return this;
    }

    /// <summary>
    ///     Uses a custom preference provider. Registered scoped, so it may depend on a DbContext.
    /// </summary>
    public NotificationsBuilder UsePreferences<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>()
        where TProvider : class, INotificationPreferenceProvider
    {
        Services.AddScoped<INotificationPreferenceProvider, TProvider>();
        return this;
    }

    /// <summary>
    ///     Uses a custom recipient resolver — the extension point that turns a user id, role or tenant
    ///     into concrete delivery addresses. Registered scoped, so it may depend on a DbContext.
    /// </summary>
    public NotificationsBuilder UseRecipientResolver<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TResolver>()
        where TResolver : class, IRecipientResolver
    {
        Services.AddScoped<IRecipientResolver, TResolver>();
        return this;
    }

    /// <summary>Configures notification options.</summary>
    public NotificationsBuilder Configure(Action<NotificationOptions> configure)
    {
        Services.Configure(configure);
        return this;
    }
}
