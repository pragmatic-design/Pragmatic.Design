using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Configuration.Database.Postgres;

/// <summary>
///     DI registration for PostgreSQL native configuration-change push (LISTEN/NOTIFY).
/// </summary>
public static class PostgresConfigurationWatchExtensions
{
    /// <summary>
    ///     Enables low-latency configuration watching on PostgreSQL: the database store consumes the
    ///     registered <see cref="IConfigurationChangeNotifier" /> for push updates (including DELETEs) while
    ///     still polling periodically as a reconcile safety net. Requires the schema created by
    ///     <c>AddDatabaseConfigurationStore</c> (which installs the notify trigger) and an Npgsql
    ///     <see cref="IDbConnectionFactory" />.
    /// </summary>
    public static IServiceCollection AddPostgresConfigurationWatch(this IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigurationChangeNotifier, PostgresConfigurationChangeNotifier>());
        return services;
    }
}
