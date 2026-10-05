using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Extensions;

/// <summary>
/// Extension methods for configuring Pragmatic.Logging services in the DI container.
/// </summary>
public static class PragmaticLoggingServiceCollectionExtensions
{
    /// <param name="services">The service collection</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds Pragmatic.Logging services to the service collection.
        /// </summary>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLogging()
        {
            return services.AddPragmaticLoggingBuilder(_ => { });
        }

        /// <summary>
        /// Adds Pragmatic.Logging services to the service collection with configuration.
        /// </summary>
        /// <param name="configure">Configuration action</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLoggingBuilder(Action<PragmaticLoggingBuilder> configure)
        {
            return services.AddPragmaticLogging(null, configure);
        }

        /// <summary>
        /// Adds Pragmatic.Logging services with provider configuration only.
        /// </summary>
        /// <param name="configure">Provider configuration action</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLogging(Action<PragmaticLoggingBuilder> configure)
        {
            return services.AddPragmaticLogging(null, null, configure);
        }

        /// <summary>
        /// Adds Pragmatic.Logging services with global filter configuration and provider setup.
        /// </summary>
        /// <param name="configureGlobal">Global filter configuration action</param>
        /// <param name="configure">Provider configuration action</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLogging(Action<GlobalFilterConfiguration>? configureGlobal,
            Action<PragmaticLoggingBuilder> configure)
        {
            return services.AddPragmaticLogging(null, configureGlobal, configure);
        }

        /// <summary>
        /// Adds Pragmatic.Logging services with an existing ILoggerFactory and provider setup.
        /// </summary>
        /// <param name="existingFactory">Existing ILoggerFactory to preserve (null to auto-detect)</param>
        /// <param name="configureGlobal">Global filter configuration action</param>
        /// <param name="configure">Provider configuration action</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLogging(ILoggerFactory? existingFactory,
            Action<GlobalFilterConfiguration>? configureGlobal,
            Action<PragmaticLoggingBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            // Register core services
            services.TryAddSingleton<PragmaticLoggerProviderRegistry>();
            services.TryAddSingleton<ContextManager>();

            // Configure global filtering
            var globalConfig = new GlobalFilterConfiguration();
            configureGlobal?.Invoke(globalConfig);
            services.AddSingleton(globalConfig);

            // Replace the standard ILoggerFactory with our enhanced one that preserves existing providers
            services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(serviceProvider =>
            {
                // Use provided factory or auto-detect existing one
                var innerFactory = existingFactory ?? GetExistingLoggerFactory(serviceProvider);
                var providerRegistry = serviceProvider.GetRequiredService<PragmaticLoggerProviderRegistry>();
                var globalFilters = serviceProvider.GetRequiredService<GlobalFilterConfiguration>();

                // Auto-populate registry from all IPragmaticLoggerProvider instances registered in DI
                foreach (var provider in serviceProvider.GetServices<IPragmaticLoggerProvider>())
                {
                    providerRegistry.RegisterProvider(provider);
                }

                return new PragmaticLoggerFactory(innerFactory, providerRegistry, globalFilters);
            }));

            // Create and configure the builder
            var builder = new PragmaticLoggingBuilder(services);
            configure(builder);

            return services;
        }

        /// <summary>
        /// Adds a Pragmatic.Logging provider to the service collection.
        /// </summary>
        /// <typeparam name="TProvider">The provider type</typeparam>
        /// <param name="factory">Factory function to create the provider</param>
        /// <returns>The service collection for chaining</returns>
        /// <remarks>
        ///     The one place every provider passes through, the built-in ones (through the overload with a
        ///     configuration) and a custom one added with <c>AddProvider&lt;TProvider&gt;</c> alike. That is
        ///     why the declared redactor and the JSON seam are attached here rather than threaded through
        ///     each provider's constructor: a custom provider that went around this got neither, and outside
        ///     the composed host nothing else masked the declared members it wrote.
        /// </remarks>
        public IServiceCollection AddPragmaticProvider<TProvider>(Func<IServiceProvider, TProvider> factory)
            where TProvider : class, IPragmaticLoggerProvider
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(factory);

            // One per container, built over whatever IRedactionMap the generators contributed.
            // Registered here rather than in a separate Add* call because a provider is the only thing
            // that consumes it, and a redactor nobody wires redacts nothing.
            services.TryAddSingleton<global::Pragmatic.Redaction.DeclaredRedactor>();

            services.AddSingleton<TProvider>(serviceProvider =>
            {
                var provider = factory(serviceProvider);

                if (provider is Providers.PragmaticLoggerProviderBase withRedaction)
                {
                    withRedaction.DeclaredRedactor =
                        serviceProvider.GetService(typeof(global::Pragmatic.Redaction.DeclaredRedactor))
                            as global::Pragmatic.Redaction.DeclaredRedactor;

                    // The seam the application registered its generated contexts in: a complex value is
                    // serialized from that metadata, which is what still works under Native AOT.
                    withRedaction.JsonOptions =
                        serviceProvider.GetService(typeof(global::Pragmatic.Serialization.PragmaticJsonOptions))
                            as global::Pragmatic.Serialization.PragmaticJsonOptions;
                }

                return provider;
            });

            // Register the provider with the registry during container build
            services.AddSingleton<IPragmaticLoggerProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<TProvider>());

            return services;
        }

        /// <summary>
        /// Adds a Pragmatic.Logging provider to the service collection with configuration.
        /// </summary>
        /// <typeparam name="TProvider">The provider type</typeparam>
        /// <param name="configuration">The provider configuration</param>
        /// <param name="factory">Factory function to create the provider</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticProvider<TProvider>(IPragmaticProviderConfiguration configuration,
            Func<IServiceProvider, IPragmaticProviderConfiguration, TProvider> factory)
            where TProvider : class, IPragmaticLoggerProvider
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(factory);

            return services.AddPragmaticProvider<TProvider>(serviceProvider => factory(serviceProvider, configuration));
        }

        /// <summary>
        /// Adds Pragmatic.Logging providers as augmentation to existing logging infrastructure.
        /// This mode preserves the existing ILoggerFactory and adds Pragmatic providers alongside it.
        /// </summary>
        /// <param name="configure">Provider configuration action</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLoggingAugmentation(Action<PragmaticLoggingBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            // Register core services
            services.TryAddSingleton<PragmaticLoggerProviderRegistry>();
            services.TryAddSingleton<ContextManager>();

            // Configure default global filtering
            var globalConfig = new GlobalFilterConfiguration();
            services.TryAddSingleton(globalConfig);

            // In augmentation mode, we DON'T replace ILoggerFactory
            // Instead, we register our providers as standard ILoggerProvider instances
            var builder = new PragmaticLoggingBuilder(services);
            configure(builder);

            // Add a hosted service to register Pragmatic providers as standard ILoggerProvider
            services.AddHostedService<PragmaticProviderIntegrationService>();

            return services;
        }
    }

    /// <summary>
    /// Gets the existing ILoggerFactory from the service provider or creates a new one with existing providers.
    /// </summary>
    /// <param name="serviceProvider">The service provider</param>
    /// <returns>An ILoggerFactory that preserves existing providers</returns>
    private static ILoggerFactory GetExistingLoggerFactory(IServiceProvider serviceProvider)
    {
        // Try to get an existing ILoggerFactory from the container without creating PragmaticLoggerFactory
        // This is tricky because we're replacing the registration, so we need to look for the underlying providers

        // First, try to get ILoggerProvider instances that were registered.
        // Filter out our own factory by Type identity (not name string) to avoid a circular
        // dependency. typeof comparison is exact and refactor-safe, unlike GetType().Name.
        var existingProviders = serviceProvider.GetServices<ILoggerProvider>()
            .Where(p => p.GetType() != typeof(PragmaticLoggerFactory))
            .ToList();

        if (existingProviders.Any())
        {
            // Create a new LoggerFactory with the existing providers
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            foreach (var provider in existingProviders)
            {
                loggerFactory.AddProvider(provider);
            }
            return loggerFactory;
        }

        // Fallback to NullLoggerFactory if no existing providers are found
        return new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory();
    }
}

/// <summary>
/// Builder for configuring Pragmatic.Logging services: the providers here, the global options and
/// presets in <c>PragmaticLoggingBuilder.Options.cs</c>.
/// </summary>
public sealed partial class PragmaticLoggingBuilder
{
    /// <summary>
    /// Gets the service collection being configured.
    /// </summary>
    public IServiceCollection Services { get; }

    internal PragmaticLoggingBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// Configures context enrichment: which system providers the context manager carries, and the
    /// application's own providers beside them.
    /// </summary>
    /// <param name="configure">Configuration action</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    ///     ⚠️ The configuration is applied here, not registered as options: the context manager is built
    ///     from it. <c>Services.Configure&lt;ContextConfiguration&gt;</c> would register options nothing
    ///     reads, and every switch an application wrote would be inert.
    /// </remarks>
    public PragmaticLoggingBuilder ConfigureContext(Action<ContextConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var configuration = new ContextConfiguration();
        configure(configuration);

        ContextEnrichmentRegistration.Apply(Services, configuration);
        return this;
    }

    /// <summary>
    /// Adds a console provider with default configuration.
    /// </summary>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddConsole()
    {
        return AddConsole(PragmaticConsoleConfiguration.ForAdvancedConsole());
    }

    /// <summary>
    /// Adds a console provider with custom configuration.
    /// </summary>
    /// <param name="configuration">The provider configuration</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddConsole(IPragmaticProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Services.AddPragmaticProvider(configuration, (serviceProvider, config) =>
            new PragmaticConsoleProvider("Console", config));

        return this;
    }

    /// <summary>
    /// Adds a console provider with console-specific custom properties.
    /// </summary>
    /// <param name="useColors">Whether to use colored output</param>
    /// <param name="truncateCategories">Whether to truncate long category names</param>
    /// <param name="maxCategoryLength">Maximum category name length</param>
    /// <param name="useStdErrorForErrors">Whether to write errors to stderr</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddConsole(
        bool useColors = true,
        bool truncateCategories = true,
        int maxCategoryLength = 40,
        bool useStdErrorForErrors = false)
    {
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        return AddConsole(config);
    }

    /// <summary>
    /// Adds a console provider with general configuration action.
    /// </summary>
    /// <param name="configure">Configuration action</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddConsole(Action<PragmaticProviderConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        configure(config);

        return AddConsole(config);
    }

    /// <summary>
    /// Adds a file provider with default configuration.
    /// </summary>
    /// <param name="filePath">The log file path</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddFile(string filePath)
    {
        return AddFile(filePath, PragmaticProviderConfiguration.ForFile());
    }

    /// <summary>
    /// Adds a file provider with custom configuration.
    /// </summary>
    /// <param name="filePath">The log file path</param>
    /// <param name="configuration">The provider configuration</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddFile(string filePath, IPragmaticProviderConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(configuration);

        Services.AddPragmaticProvider(configuration, (serviceProvider, config) =>
            new PragmaticFileProvider("File", config, filePath));

        return this;
    }

    /// <summary>
    /// Adds a file provider with configuration action.
    /// </summary>
    /// <param name="filePath">The log file path</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddFile(string filePath, Action<PragmaticProviderConfiguration> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(configure);

        var config = PragmaticProviderConfiguration.ForFile();
        configure(config);

        return AddFile(filePath, config);
    }

    /// <summary>
    /// Adds a JSON provider with default configuration.
    /// </summary>
    /// <param name="output">The output stream or file path</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddJson(string output)
    {
        return AddJson(output, PragmaticJsonConfiguration.ForJson());
    }

    /// <summary>
    /// Adds a JSON provider with custom configuration.
    /// </summary>
    /// <param name="output">The output stream or file path</param>
    /// <param name="configuration">The provider configuration</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddJson(string output, IPragmaticProviderConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(configuration);

        Services.AddPragmaticProvider(configuration, (serviceProvider, config) =>
            new PragmaticJsonProvider("Json", config, output));

        return this;
    }

    /// <summary>
    /// Adds a JSON provider with configuration action.
    /// </summary>
    /// <param name="output">The output stream or file path</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddJson(string output, Action<PragmaticProviderConfiguration> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(configure);

        var config = PragmaticJsonConfiguration.ForJson();
        configure(config);

        return AddJson(output, config);
    }

    /// <summary>
    /// Adds the enhanced JSON provider with NDJSON and async buffering support.
    /// </summary>
    /// <param name="filePath">The file path for NDJSON output</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddNdjsonAsync(string filePath)
    {
        return AddNdjsonAsync(filePath, PragmaticEnhancedJsonConfiguration.ForNdjsonAsync());
    }

    /// <summary>
    /// Adds the enhanced JSON provider with custom configuration.
    /// </summary>
    /// <param name="filePath">The file path for NDJSON output</param>
    /// <param name="configuration">The provider configuration</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddNdjsonAsync(string filePath, IPragmaticProviderConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(configuration);

        Services.AddPragmaticProvider(configuration, (serviceProvider, config) =>
            new PragmaticEnhancedJsonProvider("EnhancedJson", config, filePath));

        return this;
    }

    /// <summary>
    /// Adds the enhanced JSON provider with configuration action.
    /// </summary>
    /// <param name="filePath">The file path for NDJSON output</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddNdjsonAsync(string filePath, Action<PragmaticProviderConfiguration> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(configure);

        var config = PragmaticEnhancedJsonConfiguration.ForNdjsonAsync();
        configure(config);

        return AddNdjsonAsync(filePath, config);
    }

    /// <summary>
    /// Adds a custom provider.
    /// </summary>
    /// <typeparam name="TProvider">The provider type</typeparam>
    /// <param name="factory">Factory function to create the provider</param>
    /// <returns>The builder for chaining</returns>
    public PragmaticLoggingBuilder AddProvider<TProvider>(Func<IServiceProvider, TProvider> factory)
        where TProvider : class, IPragmaticLoggerProvider
    {
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddPragmaticProvider(factory);
        return this;
    }
}

/// <summary>
/// JSON-specific configuration placeholder.
/// </summary>
public static class PragmaticJsonConfiguration
{
    /// <summary>
    /// Creates a JSON configuration with JSON-optimized defaults.
    /// </summary>
    /// <returns>JSON-optimized configuration</returns>
    public static PragmaticProviderConfiguration ForJson()
    {
        return new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Debug,
            IncludeStructuredProperties = true,
            IncludeContextEnrichment = true,
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ",
                UseUtcTimestamp = true,
                MessageTemplate = "{\"timestamp\":\"{Timestamp}\",\"level\":\"{Level}\",\"category\":\"{Category}\",\"message\":\"{Message}\"}",
                IncludeExceptionDetails = true,
                PrettyPrintJson = true
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = true,
                UseZeroAllocation = true
            },
            ContextFilter = new ContextFilterConfiguration
            {
                Mode = ContextFilterMode.Include,
                PropertyNames = new HashSet<string>()
            },
            CustomProperties = new Dictionary<string, object?>
            {
                ["StructuredOutput"] = true,
                ["EscapeJson"] = true
            }
        };
    }
}
