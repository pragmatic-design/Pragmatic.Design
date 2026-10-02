using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications.Configuration;
using Pragmatic.Notifications.Tracking;

namespace Pragmatic.Notifications.EFCore;

/// <summary>
///     Builder extension for EF Core notification store.
/// </summary>
public static class NotificationEFCoreExtensions
{
    /// <summary>
    ///     Uses EF Core backed notification store.
    ///     <paramref name="configureDb"/> is required to configure the database provider (e.g. UseNpgsql, UseSqlite).
    ///     If omitted, an <see cref="InvalidOperationException"/> is thrown at startup when
    ///     <see cref="IDbContextFactory{TContext}"/> cannot be resolved.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>It does not create the table.</b> This registers a context of the package's own, and a
    ///     Pragmatic host creates one context per <b>declared database</b> — the migration context —
    ///     and nothing else, so <c>__Notifications</c> is not among them and the first send fails at
    ///     run time. Today the caller creates the schema, which is what the package's sample does
    ///     three lines after calling this; the shape that belongs in a composed host is
    ///     <see cref="NotificationDbContext.ApplyNotificationConfigurations" />, mapping the table into
    ///     the application's own context and migrations.
    /// </remarks>
    public static NotificationsBuilder UseEfCoreStore(
        this NotificationsBuilder builder,
        Action<DbContextOptionsBuilder> configureDb)
    {
        builder.Services.AddDbContextFactory<NotificationDbContext>(configureDb);
        builder.Services.AddSingleton<INotificationStore, EfCoreNotificationStore>();
        return builder;
    }
}
