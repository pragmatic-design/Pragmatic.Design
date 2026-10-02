using Pragmatic.Composition;
using Pragmatic.Migrations.Configuration;

namespace Pragmatic.Migrations.Extensions;

/// <summary>
///     Extension methods for configuring Pragmatic Migrations via <see cref="IPragmaticBuilder" />.
/// </summary>
public static class PragmaticBuilderMigrationsExtensions
{
    extension(IPragmaticBuilder builder)
    {
        /// <summary>
        ///     Enables Pragmatic Migrations for all databases in this host.
        ///     Providers are auto-detected from <c>SchemaVersion.ProviderName</c> (SG-generated).
        /// </summary>
        public IPragmaticBuilder UsePragmaticMigrations()
        {
            var migrationsBuilder = new MigrationsBuilder(builder.Services);
            migrationsBuilder.Build();
            return builder;
        }

        /// <summary>
        ///     Enables Pragmatic Migrations with configuration.
        ///     Use <c>OnlyDatabase&lt;T&gt;()</c> to filter which databases to migrate.
        /// </summary>
        public IPragmaticBuilder UsePragmaticMigrations(Action<MigrationsBuilder> configure)
        {
            var migrationsBuilder = new MigrationsBuilder(builder.Services);
            configure(migrationsBuilder);
            migrationsBuilder.Build();
            return builder;
        }
    }
}
