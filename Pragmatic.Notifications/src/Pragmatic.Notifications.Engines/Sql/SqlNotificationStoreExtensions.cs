using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications.Engines.Sql;

/// <summary>
///     The notification store of a <c>[StoresNotifications]</c> boundary the Sql engine serves. Called by the boundary's generated
///     <c>Add{Boundary}Persistence</c>: the store over the boundary's session, in place of the in-memory one Pragmatic.Notifications registers by
///     default. The table is in the database's schema, with the boundary's.
/// </summary>
public static class SqlNotificationStoreExtensions
{
    /// <summary>The notification store over <typeparamref name="TBoundary" />'s session.</summary>
    public static IServiceCollection AddSqlNotificationStore<TBoundary>(this IServiceCollection services)
        => services.AddSingleton<INotificationStore>(sp => new SqlNotificationStore(sp.GetRequiredService<IServiceScopeFactory>(), typeof(TBoundary),
            sp.GetRequiredService<ILogger<SqlNotificationStore>>(), sp.GetService<TimeProvider>()));
}
