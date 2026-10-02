using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.FeatureFlags.Providers;

namespace Pragmatic.FeatureFlags;

/// <summary>
///     Extension methods for registering Pragmatic.FeatureFlags services.
/// </summary>
public static class FeatureFlagServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds feature flag services with default in-memory store.
        ///     The store can be replaced by registering a custom <see cref="IFeatureFlagStore"/> before calling this.
        /// </summary>
        public IServiceCollection AddPragmaticFeatureFlags()
        {
            // Register concrete + interface against the same instance so seeders that need
            // the mutable API (InMemoryFeatureFlagStore.Define) can resolve the concrete
            // directly without downcasting an IFeatureFlagStore that may have been wrapped.
            services.TryAddSingleton<InMemoryFeatureFlagStore>();
            services.TryAddSingleton<IFeatureFlagStore>(sp => sp.GetRequiredService<InMemoryFeatureFlagStore>());
            services.AddContextualFeatureFlags();
            return services;
        }

        /// <summary>
        ///     Adds feature flag services with a custom store factory.
        /// </summary>
        /// <remarks>
        ///     ⚠️ It <b>replaces</b>, where the overload above tries. The difference is who is
        ///     speaking: that one registers the default and says in its own summary that a store
        ///     registered before it wins, so yielding is its contract. Naming a store is a caller's
        ///     explicit choice. A <c>TryAdd</c> here would lose whenever the parameterless overload had
        ///     run first, and the generated composition runs it first every time. Measured, not reasoned about: the case is
        ///     <c>TheTypedOverload_WinsOverTheFrameworkDefault</c>.
        /// </remarks>
        public IServiceCollection AddPragmaticFeatureFlags<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>()
            where TStore : class, IFeatureFlagStore
        {
            services.Replace(ServiceDescriptor.Singleton<IFeatureFlagStore, TStore>());
            services.AddContextualFeatureFlags();
            return services;
        }

        /// <summary>
        ///     Registers <see cref="IFeatureFlags"/>, which evaluates flags against the ambient context so
        ///     callers do not have to resolve <see cref="IFeatureFlagContextProvider"/> themselves.
        /// </summary>
        /// <remarks>
        ///     Scoped, because a context provider typically reads per-request state (tenant, user). The
        ///     provider is resolved optionally: an app that has not registered one still gets a working
        ///     <see cref="IFeatureFlags"/> that evaluates against <see cref="FeatureFlagContext.Empty"/>.
        ///     Called by both <c>AddPragmaticFeatureFlags</c> overloads; safe to call directly when the store
        ///     is registered by other means (for example the store shipped by another package).
        /// </remarks>
        public IServiceCollection AddContextualFeatureFlags()
        {
            services.TryAddScoped<IFeatureFlags>(sp => new ContextualFeatureFlags(
                sp.GetRequiredService<IFeatureFlagStore>(),
                sp.GetService<IFeatureFlagContextProvider>()));
            return services;
        }
    }
}
