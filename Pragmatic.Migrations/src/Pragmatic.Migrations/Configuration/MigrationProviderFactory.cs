using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Configuration;

/// <summary>
///     Resolves the correct SQL generator, schema introspector, and connection factory for a given provider name.
///     Supports multi-database setups where different databases use different providers.
/// </summary>
public sealed class MigrationProviderFactory(IServiceProvider serviceProvider)
{
    /// <summary>Gets the SQL generator for the specified provider.</summary>
    public ISqlMigrationGenerator GetGenerator(string? providerName)
    {
        if (providerName is not null)
        {
            var keyed = serviceProvider.GetKeyedService<ISqlMigrationGenerator>(providerName);
            if (keyed is not null) return keyed;
        }

        return Resolve<ISqlMigrationGenerator>(providerName);
    }

    /// <summary>Gets the schema introspector for the specified provider.</summary>
    public ISchemaIntrospector GetIntrospector(string? providerName)
    {
        if (providerName is not null)
        {
            var keyed = serviceProvider.GetKeyedService<ISchemaIntrospector>(providerName);
            if (keyed is not null) return keyed;
        }

        return Resolve<ISchemaIntrospector>(providerName);
    }

    /// <summary>Gets the connection factory for the specified provider.</summary>
    public IConnectionFactory GetConnectionFactory(string? providerName)
    {
        if (providerName is not null)
        {
            var keyed = serviceProvider.GetKeyedService<IConnectionFactory>(providerName);
            if (keyed is not null) return keyed;
        }

        return Resolve<IConnectionFactory>(providerName);
    }

    // Resolves the default (non-keyed) service, translating the DI framework's opaque
    // InvalidOperationException into one that names the requested provider — the most common
    // misconfiguration is a missing/typo'd provider key, which the raw DI message does not surface.
    private T Resolve<T>(string? providerName) where T : notnull
    {
        // Each provider registration also sets the default, so with several registered the default is whichever came last: a schema
        // that names none would migrate through a database it does not use, and fail with that database's error (#441).
        if (providerName is null)
        {
            var registered = serviceProvider.GetServices<IConnectionFactory>()
                .Select(f => f.ProviderName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (registered.Count > 1)
                throw new InvalidOperationException(
                    $"The schema names no provider and {registered.Count} are registered: {string.Join(", ", registered)}. " +
                    $"Set {nameof(SchemaVersion)}.{nameof(SchemaVersion.ProviderName)} to the one this database uses.");
        }

        var service = serviceProvider.GetService<T>();
        if (service is not null) return service;

        var provider = providerName ?? "(default)";
        throw new InvalidOperationException(
            $"No {typeof(T).Name} registered for provider '{provider}'. " +
            "Ensure the provider's NuGet package is referenced and registered (e.g. via UseProvider), " +
            "and that the provider name matches one of: PostgreSql, SqlServer, Sqlite, MySql, Oracle.");
    }
}
