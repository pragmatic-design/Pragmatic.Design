using Pragmatic.Composition;
using Pragmatic.Composition.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.MultiTenancy;

/// <summary>
///     Extension methods for configuring multi-tenancy on <see cref="IPragmaticBuilder" />.
/// </summary>
public static class PragmaticBuilderMultiTenancyExtensions
{
    /// <param name="builder">The Pragmatic builder.</param>
    extension(IPragmaticBuilder builder)
    {
        /// <summary>
        ///     Configures the multi-tenancy resolution strategy.
        ///     Overrides the SG default (single-tenant mode).
        /// </summary>
        /// <param name="configure">Builder to configure tenant resolution (UseHeader, UseClaim, UseSubdomain, etc.).</param>
        /// <returns>The builder for chaining.</returns>
        public IPragmaticBuilder UseMultiTenancy(Action<MultiTenancyBuilder> configure)
        {
            // Registering the resolver is not enough: without the middleware nothing ever resolves,
            // rows are written with an empty tenant id and read back as nothing, in silence. The step
            // therefore goes on inside AddPragmaticMultiTenancy, which the generator also calls on its
            // own — registering it only here left the generator's default path unresolved.
            builder.Services.AddPragmaticMultiTenancy(configure);
            return builder;
        }

        /// <summary>
        ///     Enables multi-tenancy with the default single-tenant resolver
        ///     (tenant id <c>"default"</c>). Convenience overload for apps that
        ///     want the multi-tenancy infrastructure wired without committing to a
        ///     resolution strategy up-front.
        /// </summary>
        /// <returns>The builder for chaining.</returns>
        public IPragmaticBuilder UseMultiTenancy()
            => builder.UseMultiTenancy(b => b.UseSingleTenant());
    }
}
