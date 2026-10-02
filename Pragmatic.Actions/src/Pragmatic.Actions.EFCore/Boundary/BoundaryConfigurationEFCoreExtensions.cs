using Microsoft.EntityFrameworkCore;
using Pragmatic.Actions.Boundary;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Actions.EFCore;

/// <summary>
///     EF Core extension methods for <see cref="BoundaryConfiguration{TBoundary}" />.
/// </summary>
public static class BoundaryConfigurationEFCoreExtensions
{
    /// <param name="configuration">The boundary configuration.</param>
    /// <typeparam name="TBoundary">The boundary marker type.</typeparam>
    extension<TBoundary>(BoundaryConfiguration<TBoundary> configuration) where TBoundary : IBoundary
    {
        /// <summary>
        ///     Configures the database provider for local boundaries using EF Core.
        /// </summary>
        /// <param name="configure">The action to configure DbContext options.</param>
        /// <returns>The configuration instance for fluent chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown when called on a remote boundary.</exception>
        /// <example>
        ///     <code>
        /// cfg.UseLocal().UseDatabase(opt => opt.UseNpgsql(connectionString));
        /// cfg.UseLocal().UseDatabase(opt => opt.UseSqlServer(connectionString));
        /// </code>
        /// </example>
        public BoundaryConfiguration<TBoundary> UseDatabase(Action<DbContextOptionsBuilder> configure)
        {
            if (configuration.Mode == BoundaryMode.Remote)
                throw new InvalidOperationException(
                    "Cannot configure database for a remote boundary. " +
                    "Remote boundaries use HTTP clients instead of database connections.");

            ThrowIfNull(configure);
            configuration.DatabaseOptions = configure;
            return configuration;
        }

        /// <summary>
        ///     Gets the typed DbContext options builder action.
        /// </summary>
        /// <returns>The DbContext options action, or null if not configured.</returns>
        public Action<DbContextOptionsBuilder>? GetDbContextOptions()
        {
            return configuration.DatabaseOptions as Action<DbContextOptionsBuilder>;
        }
    }
}
