namespace Pragmatic.Configuration.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Pragmatic.Caching;
using Pragmatic.Configuration.Cache;
using Pragmatic.Configuration.Options;
using Pragmatic.Configuration.Providers;
using Pragmatic.Configuration.Resolution;
using Pragmatic.Configuration.Startup;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;

/// <summary>
///     Extension methods for registering Pragmatic.Configuration services.
/// </summary>
public static class ConfigurationServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds Pragmatic configuration services: resolver and default stores.
        ///     Default stores are in-memory and can be replaced by database or cloud backends.
        /// </summary>
        public IServiceCollection AddPragmaticConfiguration(Action<PragmaticConfigurationOptions>? configure = null)
        {
            // Registered once. The resolver and the caching decision read the options of the call that
            // registered them, so a second call's options would change nothing — turning the tenant layer
            // on there would be ignored in silence, hence the throw. With nothing to configure, a second
            // call loses nothing.
            if (services.Any(d => d.ServiceType == typeof(PragmaticConfigurationOptions)))
            {
                if (configure is null)
                    return services;

                throw new InvalidOperationException(
                    "AddPragmaticConfiguration has already been called, and the options of a second call would be "
                    + "ignored: the resolver keeps those of the first. Configure everything in one call.");
            }

            var options = new PragmaticConfigurationOptions();
            configure?.Invoke(options);

            // Register the configured instance directly (existing consumers rely on this)
            // and via the options pattern so IOptions<PragmaticConfigurationOptions> resolves
            // the same configured values.
            services.AddSingleton(options);
            services.AddOptions<PragmaticConfigurationOptions>();
            services.Configure<PragmaticConfigurationOptions>(configure ?? (_ => { }));

            // The catalogue of declared settings, empty until an assembly contributes to it. The generated
            // registration looks for this same instance and adds its sections, whichever runs first — so
            // an application that declares no [Configuration] section has a catalogue that says so,
            // rather than none, and a host without one is not refused at startup for the absence.
            if (!services.Any(d => d.ServiceType == typeof(Discovery.ConfigurationCatalog)))
            {
                var catalog = new Discovery.ConfigurationCatalog();
                services.AddSingleton(catalog);
                services.AddSingleton<Discovery.IConfigurationCatalog>(catalog);
            }

            // Default stores (can be replaced by DB/Azure/Vault backends)
            services.TryAddSingleton<IConfigurationStore, InMemoryConfigurationStore>();
            services.TryAddSingleton<ISecretStore, InMemorySecretStore>();

            // Sensitive-key classifier. The generated aggregator (Add{Prefix}Configuration) ALWAYS
            // supersedes this — with the compile-time set when an assembly declares [Sensitive]
            // properties, and with NullSensitiveKeyClassifier when it declares none. So this default
            // is only reached when the aggregator is absent, which means the generator did not run,
            // and then nothing knows which keys are secret. It fails closed: every key sensitive,
            // because answering "not sensitive" turns the write guard and the audit masking into
            // no-ops and puts secrets in cleartext in an append-only trail.
            services.TryAddSingleton<ISensitiveKeyClassifier>(UnavailableSensitiveKeyClassifier.Instance);

            // Write access is a separate, privileged contract. It resolves whatever ISecretStore is
            // effective (after any caching decoration/backend override); a read-only backend surfaces a
            // clear error instead of silently no-op'ing. One registration covers every backend.
            services.TryAddSingleton<IWritableSecretStore>(sp =>
                sp.GetRequiredService<ISecretStore>() as IWritableSecretStore
                ?? throw new InvalidOperationException(
                    "The registered ISecretStore is read-only. Register a writable backend " +
                    "(e.g. AddDatabaseSecretStore) to use IWritableSecretStore."));

            // Read-through caching, default-on. Falls back to an in-process cache stack when
            // Pragmatic.Caching is not referenced; a host-registered ICacheStack (e.g. HybridCacheStack) wins.
            if (options.EnableReadCaching)
            {
                services.TryAddSingleton<ICacheStack, InMemoryConfigurationCacheStack>();
                // Marker enabling order-independent (re-)decoration by backends registered later.
                services.TryAddSingleton<ConfigurationCachingEnabledMarker>();
                services.DecorateConfigurationStoreWithCaching();
                services.DecorateSecretStoreWithCaching();
            }

            // Environment profile (resolved from IHostEnvironment)
            services.TryAddSingleton(sp =>
            {
                var hostEnv = sp.GetService<IHostEnvironment>();
                var envName = hostEnv?.EnvironmentName ?? "Production";
                return EnvironmentProfile.From(envName, options.EnvironmentTag);
            });

            // Configuration resolver (scoped because ITenantContext is scoped). The base cascade is
            // wrapped by SecretResolvingConfigurationResolver so a resolved secret://{key} reference is
            // fetched locally from ISecretStore at read time — the config store (and any distributed
            // fabric backing it) only ever holds the pointer, never the secret material.
            services.TryAddScoped<IConfigurationResolver>(sp =>
            {
                var store = sp.GetRequiredService<IConfigurationStore>();
                var env = sp.GetRequiredService<EnvironmentProfile>();
                var tenantContext = options.MultiTenant.Enabled
                    ? sp.GetService<ITenantContext>()
                    : null;

                // User-scoped overrides are applied whenever an ICurrentUser is available in the
                // request scope (optional: anonymous/no-identity hosts simply skip the user tier).
                var currentUser = sp.GetService<ICurrentUser>();

                var cascade = new ConfigurationResolver(
                    store, env, tenantContext, currentUser, options.MultiTenant.FallbackToBase);
                return new SecretResolvingConfigurationResolver(
                    cascade,
                    sp.GetRequiredService<ISecretStore>(),
                    tenantContext,
                    sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>());
            });

            // Strongly-typed per-tenant options facade over the resolver (#5).
            services.TryAddScoped(typeof(ITenantOptions<>), typeof(TenantOptions<>));

            return services;
        }

        /// <summary>
        ///     Registers a typed handler invoked whenever a key under <typeparamref name="TOptions" />'s
        ///     configuration section changes in the store. Wires the background dispatcher (once) that watches
        ///     the store and fans changes out to the matching handlers. Requires a store whose
        ///     <c>WatchAsync</c> emits changes (e.g. the database store, optionally with native push).
        /// </summary>
        public IServiceCollection AddConfigurationChangeHandler<TOptions, THandler>()
            where TOptions : class
            where THandler : class, IConfigurationChangeHandler<TOptions>
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IConfigurationChangeHandler<TOptions>, THandler>());

            // Capture TOptions statically here so the dispatcher fans out with zero reflection.
            var section = ConfigurationSectionResolver.Resolve<TOptions>();
            services.AddSingleton(new ConfigurationChangeSubscription(section, static async (sp, change, ct) =>
            {
                foreach (var handler in sp.GetServices<IConfigurationChangeHandler<TOptions>>())
                    await handler.OnChangedAsync(change, ct).ConfigureAwait(false);
            }));

            // Register the dispatcher once (TryAddEnumerable dedups by implementation type).
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, ConfigurationChangeDispatcher>());

            return services;
        }

        /// <summary>
        ///     Declares configuration keys that must resolve to a value at startup; the host fails fast (throws
        ///     during start) listing any that are unset. Complements options <c>ValidateOnStart</c> — use this
        ///     for raw keys read directly via <see cref="IConfigurationStore" /> (e.g. a secret name) that no
        ///     bound POCO covers. Accumulates across calls.
        /// </summary>
        public IServiceCollection AddRequiredConfiguration(params string[] keys)
        {
            // Accumulate into a single shared instance registered as an implementation instance, so repeated
            // calls append rather than replace.
            var existing = services
                .FirstOrDefault(d => d.ServiceType == typeof(RequiredConfigurationKeys))?
                .ImplementationInstance as RequiredConfigurationKeys;

            if (existing is null)
            {
                existing = new RequiredConfigurationKeys();
                services.AddSingleton(existing);
            }

            existing.Keys.AddRange(keys);

            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, RequiredConfigurationValidator>());

            return services;
        }

        /// <summary>
        ///     Wraps the currently-registered <see cref="IConfigurationStore" /> with a read-through caching
        ///     decorator. Idempotent and order-independent: backends registered after
        ///     <see cref="AddPragmaticConfiguration" /> (e.g. <c>AddDatabaseConfigurationStore</c>) should call
        ///     this again so the decorator wraps the final concrete store.
        /// </summary>
        public IServiceCollection DecorateConfigurationStoreWithCaching()
        {
            // Respect the opt-out: only decorate when AddPragmaticConfiguration enabled read caching.
            if (!services.Any(d => d.ServiceType == typeof(ConfigurationCachingEnabledMarker)))
                return services;

            // The effective registration is the last one (last-wins on a single resolve).
            var lastIndex = -1;
            for (var i = services.Count - 1; i >= 0; i--)
                if (services[i].ServiceType == typeof(IConfigurationStore))
                {
                    lastIndex = i;
                    break;
                }

            if (lastIndex < 0)
                return services;

            var inner = services[lastIndex];

            // Already decorated — nothing to do.
            if (inner.ImplementationType == typeof(CachingConfigurationStore))
                return services;

            // Build a factory that materializes the original store, then wraps it. Keeping the same
            // lifetime preserves the singleton/scoped semantics the backend chose.
            var decorated = new ServiceDescriptor(
                typeof(IConfigurationStore),
                sp =>
                {
                    var store = ResolveInner(sp, inner);
                    var cache = sp.GetRequiredService<ICacheStack>();
                    var env = sp.GetRequiredService<EnvironmentProfile>();
                    return new CachingConfigurationStore(store, cache, env);
                },
                inner.Lifetime);

            // Replace in place so the decorator remains the last (effective) registration.
            services[lastIndex] = decorated;
            return services;
        }

        /// <summary>
        ///     Secret-store twin of <see cref="DecorateConfigurationStoreWithCaching" />: wraps the
        ///     currently-registered <see cref="ISecretStore" /> with the read-through
        ///     <see cref="CachingSecretStore" /> (rotation-aware TTL). Idempotent and order-independent;
        ///     secret backends should call it again after registering their concrete store.
        /// </summary>
        public IServiceCollection DecorateSecretStoreWithCaching()
        {
            if (!services.Any(d => d.ServiceType == typeof(ConfigurationCachingEnabledMarker)))
                return services;

            var lastIndex = -1;
            for (var i = services.Count - 1; i >= 0; i--)
                if (services[i].ServiceType == typeof(ISecretStore))
                {
                    lastIndex = i;
                    break;
                }

            if (lastIndex < 0)
                return services;

            var inner = services[lastIndex];
            if (inner.ImplementationType == typeof(CachingSecretStore))
                return services;

            var decorated = new ServiceDescriptor(
                typeof(ISecretStore),
                sp =>
                {
                    var store = ResolveInnerSecret(sp, inner);
                    var cache = sp.GetRequiredService<ICacheStack>();
                    var ttl = sp.GetRequiredService<PragmaticConfigurationOptions>().SecretCacheTtl;
                    return new CachingSecretStore(store, cache, ttl);
                },
                inner.Lifetime);

            services[lastIndex] = decorated;
            return services;
        }

        /// <summary>
        ///     <b>Opt-in.</b> Wraps the currently-registered <see cref="IConfigurationStore" /> so a
        ///     <c>[Sensitive]</c> value written as plaintext (not a <c>secret://</c> reference) is warned
        ///     about — defensive-depth for any plaintext backend (the Agent config store already guards
        ///     inline). Not applied by default (it would change the resolved store type); call it
        ///     explicitly, after the concrete backend is registered, when you want the warning. Idempotent.
        /// </summary>
        public IServiceCollection DecorateConfigurationStoreWithSensitiveGuard()
        {
            var lastIndex = -1;
            for (var i = services.Count - 1; i >= 0; i--)
                if (services[i].ServiceType == typeof(IConfigurationStore))
                {
                    lastIndex = i;
                    break;
                }

            if (lastIndex < 0)
                return services;

            var inner = services[lastIndex];
            if (inner.ImplementationType == typeof(Secrets.SensitiveWriteGuardConfigurationStore))
                return services;

            var decorated = new ServiceDescriptor(
                typeof(IConfigurationStore),
                sp =>
                {
                    var store = ResolveInner(sp, inner);
                    var classifier = sp.GetService<ISensitiveKeyClassifier>() ?? NullSensitiveKeyClassifier.Instance;
                    var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<Secrets.SensitiveWriteGuardConfigurationStore>>()
                        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<Secrets.SensitiveWriteGuardConfigurationStore>.Instance;
                    return new Secrets.SensitiveWriteGuardConfigurationStore(store, classifier, logger);
                },
                inner.Lifetime);

            services[lastIndex] = decorated;
            return services;
        }
    }

    private static ISecretStore ResolveInnerSecret(IServiceProvider sp, ServiceDescriptor inner)
    {
        if (inner.ImplementationInstance is ISecretStore instance)
            return instance;

        if (inner.ImplementationFactory is not null)
            return (ISecretStore)inner.ImplementationFactory(sp);

        return (ISecretStore)ActivatorUtilities.CreateInstance(sp, inner.ImplementationType!);
    }

    private static IConfigurationStore ResolveInner(IServiceProvider sp, ServiceDescriptor inner)
    {
        if (inner.ImplementationInstance is IConfigurationStore instance)
            return instance;

        if (inner.ImplementationFactory is not null)
            return (IConfigurationStore)inner.ImplementationFactory(sp);

        // ImplementationType path: construct it with DI-resolved constructor args.
        return (IConfigurationStore)ActivatorUtilities.CreateInstance(sp, inner.ImplementationType!);
    }

    /// <summary>Presence in the container signals that configuration read-caching decoration is enabled.</summary>
    private sealed class ConfigurationCachingEnabledMarker;
}
