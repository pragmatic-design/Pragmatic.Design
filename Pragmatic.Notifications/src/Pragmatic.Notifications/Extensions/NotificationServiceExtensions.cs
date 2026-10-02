using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Configuration;
using Pragmatic.Notifications.Delivery;
using Pragmatic.Notifications.Pipeline;
using Pragmatic.Notifications.Preferences;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications.Extensions;

/// <summary>
///     DI registration for Pragmatic.Notifications.
/// </summary>
public static class NotificationServiceExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds the notification system with default configuration.
        /// </summary>
        public IServiceCollection AddPragmaticNotifications()
            => AddPragmaticNotifications(services, _ => { });

        /// <summary>
        ///     Adds the notification system with builder configuration.
        /// </summary>
        public IServiceCollection AddPragmaticNotifications(Action<NotificationsBuilder> configure)
        {
            var builder = new NotificationsBuilder(services);
            configure(builder);

            // Core services (TryAdd to allow user overrides via builder).
            //
            // Resolution-side services are SCOPED on purpose. Any realistic IRecipientResolver or
            // INotificationPreferenceProvider reads from the database — turning a user id into an
            // address, loading opt-outs — and a singleton cannot depend on a scoped DbContext without
            // capturing it. Registering these as singletons made the only useful implementations
            // impossible to write, which is why the built-in resolver never resolved a user id.
            services.TryAddScoped<INotificationService, NotificationService>();
            services.TryAddScoped<IRecipientResolver, DefaultRecipientResolver>();
            services.TryAddScoped<INotificationRouter, DefaultNotificationRouter>();
            services.TryAddScoped<INotificationPreferenceProvider, DefaultPreferenceProvider>();
            services.TryAddScoped<NotificationPipeline>();

            // Channels and the store are stateless or factory-based, so they stay singletons.
            services.TryAddSingleton<INotificationChannelFactory, DefaultChannelFactory>();
            services.TryAddSingleton<INotificationStore, InMemoryNotificationStore>();

            // Async delivery — TryAddEnumerable prevents duplicate background worker registration
            services.TryAddSingleton<NotificationDeliveryChannel>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, NotificationDeliveryWorker>());

            return services;
        }
    }
}
