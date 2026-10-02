using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Logging.Context;

/// <summary>
/// Extension methods for registering ContextManager services with dependency injection.
/// Provides various registration patterns for different application scenarios.
/// </summary>
/// <remarks>
/// <para>
/// These extensions enable easy integration of the ContextManager with .NET dependency injection,
/// replacing the singleton pattern with proper DI container management.
/// </para>
/// <para>
/// <strong>Registration Patterns:</strong>
/// </para>
/// <list type="bullet">
/// <item><description><strong>Basic</strong>: Simple registration with default providers</description></item>
/// <item><description><strong>Configured</strong>: Registration with custom provider setup</description></item>
/// <item><description><strong>Options</strong>: Integration with IOptions pattern for configuration</description></item>
/// <item><description><strong>Factory</strong>: Custom factory-based registration for complex scenarios</description></item>
/// </list>
/// </remarks>
/// <example>
/// Basic registration:
/// <code>
/// services.AddContextManager();
/// </code>
/// 
/// Registration with custom providers:
/// <code>
/// services.AddContextManager(contextManager =>
/// {
///     contextManager.RegisterProvider(new TenantContextProvider());
///     contextManager.RegisterProvider(new UserContextProvider());
/// });
/// </code>
/// 
/// Registration with options:
/// <code>
/// services.Configure&lt;ContextManagerOptions&gt;(options =>
/// {
///     options.EnableDefaultProviders = true;
///     options.CacheTimeout = TimeSpan.FromMinutes(5);
/// });
/// services.AddContextManagerWithOptions();
/// </code>
/// </example>
public static class ContextManagerServiceExtensions
{
    /// <param name="services">The service collection</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds ContextManager services to the dependency injection container with default configuration.
        /// </summary>
        /// <param name="lifetime">Service lifetime (default: Singleton)</param>
        /// <returns>The service collection for method chaining</returns>
        /// <remarks>
        /// <para>
        /// Registers:
        /// </para>
        /// <list type="bullet">
        /// <item><description><see cref="IContextManager"/> as <see cref="ContextManager"/></description></item>
        /// <item><description>Default system providers (Machine, Process, Thread)</description></item>
        /// <item><description>Singleton lifetime for performance (shared context cache)</description></item>
        /// </list>
        /// </remarks>
        public IServiceCollection AddContextManager(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            return lifetime switch
            {
                ServiceLifetime.Singleton => services.AddSingleton<IContextManager, ContextManager>(),
                ServiceLifetime.Scoped => services.AddScoped<IContextManager, ContextManager>(),
                ServiceLifetime.Transient => services.AddTransient<IContextManager, ContextManager>(),
                _ => throw new ArgumentOutOfRangeException(nameof(lifetime))
            };
        }

        /// <summary>
        /// Adds ContextManager services with custom provider registration.
        /// </summary>
        /// <param name="configure">Action to configure the context manager with custom providers</param>
        /// <param name="lifetime">Service lifetime (default: Singleton)</param>
        /// <returns>The service collection for method chaining</returns>
        /// <remarks>
        /// <para>
        /// Allows registration of custom providers during service container setup.
        /// The configure action receives the ContextManager instance after construction
        /// and before it's registered in the container.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// services.AddContextManager(contextManager =>
        /// {
        ///     contextManager.RegisterProvider(new TenantContextProvider());
        ///     contextManager.RegisterProvider(new UserContextProvider());
        ///     contextManager.RegisterProvider(new RequestTrackingProvider());
        /// });
        /// </code>
        /// </example>
        public IServiceCollection AddContextManager(Action<IContextManager> configure,
            ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            ArgumentNullException.ThrowIfNull(configure);

            return lifetime switch
            {
                ServiceLifetime.Singleton => services.AddSingleton<IContextManager>(serviceProvider =>
                {
                    var contextManager = new ContextManager();
                    configure(contextManager);
                    return contextManager;
                }),
                ServiceLifetime.Scoped => services.AddScoped<IContextManager>(serviceProvider =>
                {
                    var contextManager = new ContextManager();
                    configure(contextManager);
                    return contextManager;
                }),
                ServiceLifetime.Transient => services.AddTransient<IContextManager>(serviceProvider =>
                {
                    var contextManager = new ContextManager();
                    configure(contextManager);
                    return contextManager;
                }),
                _ => throw new ArgumentOutOfRangeException(nameof(lifetime))
            };
        }

        /// <summary>
        /// Adds ContextManager services with IOptions integration.
        /// </summary>
        /// <param name="lifetime">Service lifetime (default: Singleton)</param>
        /// <returns>The service collection for method chaining</returns>
        /// <remarks>
        /// <para>
        /// Integrates with the IOptions pattern for configuration management.
        /// Requires separate registration of ContextManagerOptions configuration.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// services.Configure&lt;ContextManagerOptions&gt;(configuration.GetSection("ContextManager"));
        /// services.AddContextManagerWithOptions();
        /// </code>
        /// </example>
        public IServiceCollection AddContextManagerWithOptions(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            services.TryAddSingleton<Microsoft.Extensions.Options.IOptionsFactory<ContextManagerOptions>,
                Microsoft.Extensions.Options.OptionsFactory<ContextManagerOptions>>();

            return lifetime switch
            {
                ServiceLifetime.Singleton => services.AddSingleton<IContextManager>(serviceProvider =>
                {
                    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ContextManagerOptions>>().Value;
                    return new ContextManager(options.EnableDefaultProviders);
                }),
                ServiceLifetime.Scoped => services.AddScoped<IContextManager>(serviceProvider =>
                {
                    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ContextManagerOptions>>().Value;
                    return new ContextManager(options.EnableDefaultProviders);
                }),
                ServiceLifetime.Transient => services.AddTransient<IContextManager>(serviceProvider =>
                {
                    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ContextManagerOptions>>().Value;
                    return new ContextManager(options.EnableDefaultProviders);
                }),
                _ => throw new ArgumentOutOfRangeException(nameof(lifetime))
            };
        }

        /// <summary>
        /// Adds ContextManager services with factory-based registration.
        /// </summary>
        /// <param name="factory">Factory function to create the context manager</param>
        /// <param name="lifetime">Service lifetime (default: Singleton)</param>
        /// <returns>The service collection for method chaining</returns>
        /// <remarks>
        /// <para>
        /// Provides full control over context manager creation using a factory function.
        /// The factory receives the service provider for resolving dependencies.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// services.AddContextManagerWithFactory(serviceProvider =>
        /// {
        ///     var logger = serviceProvider.GetRequiredService&lt;ILogger&lt;ContextManager&gt;&gt;();
        ///     var httpContextAccessor = serviceProvider.GetRequiredService&lt;IHttpContextAccessor&gt;();
        ///     
        ///     var contextManager = new ContextManager(registerDefaultProviders: true);
        ///     
        ///     // Register providers that need dependencies
        ///     contextManager.RegisterProvider(new HttpContextProvider(httpContextAccessor));
        ///     contextManager.RegisterProvider(new LoggingContextProvider(logger));
        ///     
        ///     return contextManager;
        /// });
        /// </code>
        /// </example>
        public IServiceCollection AddContextManagerWithFactory(Func<IServiceProvider, IContextManager> factory,
            ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            ArgumentNullException.ThrowIfNull(factory);

            return lifetime switch
            {
                ServiceLifetime.Singleton => services.AddSingleton(factory),
                ServiceLifetime.Scoped => services.AddScoped(factory),
                ServiceLifetime.Transient => services.AddTransient(factory),
                _ => throw new ArgumentOutOfRangeException(nameof(lifetime))
            };
        }

        /// <summary>
        /// Adds common context providers as separate services in the DI container.
        /// </summary>
        /// <returns>The service collection for method chaining</returns>
        /// <remarks>
        /// <para>
        /// Registers common context providers as services so they can be injected
        /// into other components or registered with the ContextManager separately.
        /// </para>
        /// <para>
        /// <strong>Registered Providers:</strong>
        /// </para>
        /// <list type="bullet">
        /// <item><description>MachineContextProvider</description></item>
        /// <item><description>ProcessContextProvider</description></item>
        /// <item><description>ThreadContextProvider</description></item>
        /// </list>
        /// </remarks>
        public IServiceCollection AddContextProviders()
        {
            services.TryAddTransient<Providers.MachineContextProvider>();
            services.TryAddTransient<Providers.ProcessContextProvider>();
            services.TryAddTransient<Providers.ThreadContextProvider>();

            return services;
        }

        /// <summary>
        /// Adds ContextManager with automatic discovery and registration of context providers from the DI container.
        /// </summary>
        /// <param name="lifetime">Service lifetime (default: Singleton)</param>
        /// <returns>The service collection for method chaining</returns>
        /// <remarks>
        /// <para>
        /// Automatically discovers all registered IContextProvider services in the container
        /// and registers them with the ContextManager. This enables a modular approach where
        /// different parts of the application can register their own context providers.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // Register individual providers
        /// services.AddTransient&lt;IContextProvider, TenantContextProvider&gt;();
        /// services.AddTransient&lt;IContextProvider, UserContextProvider&gt;();
        /// services.AddTransient&lt;IContextProvider, RequestContextProvider&gt;();
        /// 
        /// // Automatically discover and register all providers
        /// services.AddContextManagerWithAutoDiscovery();
        /// </code>
        /// </example>
        public IServiceCollection AddContextManagerWithAutoDiscovery(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            return lifetime switch
            {
                ServiceLifetime.Singleton => services.AddSingleton<IContextManager>(serviceProvider =>
                {
                    var contextManager = new ContextManager(registerDefaultProviders: false);

                    // Discover all IContextProvider services
                    var providers = serviceProvider.GetServices<IContextProvider>();
                    contextManager.RegisterProviders(providers);

                    return contextManager;
                }),
                ServiceLifetime.Scoped => services.AddScoped<IContextManager>(serviceProvider =>
                {
                    var contextManager = new ContextManager(registerDefaultProviders: false);

                    // Discover all IContextProvider services
                    var providers = serviceProvider.GetServices<IContextProvider>();
                    contextManager.RegisterProviders(providers);

                    return contextManager;
                }),
                ServiceLifetime.Transient => services.AddTransient<IContextManager>(serviceProvider =>
                {
                    var contextManager = new ContextManager(registerDefaultProviders: false);

                    // Discover all IContextProvider services  
                    var providers = serviceProvider.GetServices<IContextProvider>();
                    contextManager.RegisterProviders(providers);

                    return contextManager;
                }),
                _ => throw new ArgumentOutOfRangeException(nameof(lifetime))
            };
        }

        /// <summary>
        /// Registers a backward compatibility singleton accessor for existing code.
        /// </summary>
        /// <returns>The service collection for method chaining</returns>
        /// <remarks>
        /// <para>
        /// Provides a migration path for existing code that uses ContextManager.Instance.
        /// Registers a static accessor that resolves the IContextManager from the service provider.
        /// </para>
        /// <para>
        /// <strong>Usage:</strong> Add this during migration period, then gradually refactor
        /// existing code to use dependency injection.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // Enable backward compatibility
        /// services.AddContextManager();
        /// services.AddContextManagerBackwardCompatibility();
        /// 
        /// // Existing code continues to work:
        /// var context = ContextManager.Instance.GetContextProperties();
        /// 
        /// // While new code uses DI:
        /// public MyService(IContextManager contextManager) { ... }
        /// </code>
        /// </example>
        public IServiceCollection AddContextManagerBackwardCompatibility()
        {
            services.TryAddSingleton<ContextManagerSingletonAccessor>();
            return services;
        }
    }
}

/// <summary>
/// Backward compatibility accessor for ContextManager.Instance pattern.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a migration bridge for existing code that uses the singleton pattern
/// while new code adopts dependency injection.
/// </para>
/// </remarks>
public sealed class ContextManagerSingletonAccessor
{
    private readonly IContextManager _contextManager;

    private static volatile IContextManager? _instance;

    /// <summary>
    /// Gets the context manager instance from dependency injection.
    /// Write is done once during DI construction; volatile ensures visibility without a lock.
    /// </summary>
    public static IContextManager? Instance => _instance;

    /// <summary>
    /// Initializes the singleton accessor with a context manager from DI.
    /// </summary>
    /// <param name="contextManager">Context manager from dependency injection</param>
    public ContextManagerSingletonAccessor(IContextManager contextManager)
    {
        _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _instance = _contextManager;
    }
}