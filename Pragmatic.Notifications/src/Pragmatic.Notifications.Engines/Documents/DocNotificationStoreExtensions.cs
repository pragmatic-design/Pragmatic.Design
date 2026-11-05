using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications.Engines.Documents;

/// <summary>
///     The notification store of a <c>[StoresNotifications]</c> boundary the documents engine serves. Called by the boundary's generated
///     <c>Add{Boundary}Persistence</c>: the store over the boundary's session, in place of the in-memory one Pragmatic.Notifications registers by
///     default. The collection is in the boundary's <c>{Boundary}DocSchema</c> (<see cref="NotificationDocument.Schema" />).
/// </summary>
public static class DocNotificationStoreExtensions
{
    /// <summary>The notification store over <typeparamref name="TBoundary" />'s session.</summary>
    public static IServiceCollection AddDocNotificationStore<TBoundary>(this IServiceCollection services)
        => services.AddSingleton<INotificationStore>(sp => new DocNotificationStore(sp.GetRequiredService<IServiceScopeFactory>(), typeof(TBoundary),
            sp.GetRequiredService<ILogger<DocNotificationStore>>(), sp.GetService<TimeProvider>()));
}
