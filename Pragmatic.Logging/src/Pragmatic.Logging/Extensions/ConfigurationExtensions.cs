using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Configuration;
using Pragmatic.Logging.Privacy;
using Pragmatic.Logging.Privacy.Audit;
using Pragmatic.Logging.Privacy.Audit.Storage;

namespace Pragmatic.Logging.Extensions;

/// <summary>
/// Extension methods for configuring Pragmatic.Logging with IConfiguration.
/// </summary>
public static class ConfigurationExtensions
{
    /// <param name="services">The service collection</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds Pragmatic.Logging services with configuration from IConfiguration.
        /// </summary>
        /// <param name="configuration">The configuration instance</param>
        /// <param name="sectionName">The configuration section name (defaults to "PragmaticLogging")</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLoggingFromConfiguration(IConfiguration configuration,
            string sectionName = PragmaticLoggingOptions.SectionName)
        {
            // Bind configuration options
            services.Configure<PragmaticLoggingOptions>(configuration.GetSection(sectionName));

            // Add options adapter
            services.TryAddSingleton<OptionsAdapter>();

            // Register core services with configuration binding
            services.AddPragmaticLoggingCore();

            return services;
        }

        /// <summary>
        /// Adds Pragmatic.Logging services with explicit options configuration.
        /// </summary>
        /// <param name="configure">Configuration delegate</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLoggingWithOptions(Action<PragmaticLoggingOptions> configure)
        {
            services.Configure(configure);
            services.TryAddSingleton<OptionsAdapter>();
            services.AddPragmaticLoggingCore();

            return services;
        }

        /// <summary>
        /// Adds Pragmatic.Logging services with a configuration preset.
        /// </summary>
        /// <param name="preset">Configuration preset name</param>
        /// <param name="configure">Optional additional configuration</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLoggingWithPreset(string preset,
            Action<PragmaticLoggingOptions>? configure = null)
        {
            services.Configure<PragmaticLoggingOptions>(options =>
            {
                OptionsAdapter.ApplyConfigurationPreset(options, preset);
                configure?.Invoke(options);
            });

            services.TryAddSingleton<OptionsAdapter>();
            services.AddPragmaticLoggingCore();

            return services;
        }

        /// <summary>
        /// Adds core Pragmatic.Logging services.
        /// This method registers all required services based on the configured options.
        /// </summary>
        /// <remarks>
        ///     The first line is what makes the summary true. Everything below it is redaction, secret
        ///     detection and audit — the services that decorate logging, not the ones that perform it —
        ///     so six public entry points that land here (the presets, and the three overloads above)
        ///     configured options and left ILoggerFactory untouched. Two of those overloads were also
        ///     called AddPragmaticLogging, the same name as the one that does wire it, so which one a
        ///     caller got depended on the type the compiler inferred for their lambda. They are now
        ///     AddPragmaticLoggingFromConfiguration and AddPragmaticLoggingWithOptions.
        /// </remarks>
        /// <returns>The service collection for chaining</returns>
        private IServiceCollection AddPragmaticLoggingCore()
        {
            // The registry, the context manager, the global filters and the PragmaticLoggerFactory
            // that replaces ILoggerFactory. Configuring how logging should behave is not the same as
            // arranging for it to happen.
            services.AddPragmaticLogging(null, null, _ => { });

            // Add options monitoring and reactive services
            services.TryAddSingleton<IConfigurationService, ConfigurationService>();

            // Add privacy and security services conditionally.
            // SecretDetector is registered WITHOUT PragmaticAuditService to break the circular
            // dependency: SecretDetector → PragmaticAuditService → SecretDetector.
            // Audit logging inside SecretDetector is optional; callers can wire it post-construction.
            services.TryAddSingleton<SecretDetector>(provider =>
            {
                var adapter = provider.GetRequiredService<OptionsAdapter>();
                var options = adapter.ToSecretDetectionOptions();
                return new SecretDetector(options, auditService: null);
            });

            services.TryAddSingleton<PragmaticDataRedactor>(provider =>
            {
                var adapter = provider.GetRequiredService<OptionsAdapter>();
                var config = adapter.ToDataRedactorConfiguration();
                var auditService = provider.GetService<PragmaticAuditService>();
                var secretDetector = provider.GetService<SecretDetector>();
                return new PragmaticDataRedactor(config, auditService, secretDetector);
            });

            // Add audit services conditionally
            services.TryAddSingleton<Privacy.Audit.IAuditStorage>(provider =>
            {
                var adapter = provider.GetRequiredService<OptionsAdapter>();
                var auditOptions = adapter.CurrentOptions.Audit;

                return auditOptions.StorageType.ToLowerInvariant() switch
                {
                    "filesystem" => new FileSystemAuditStorage(
                        adapter.GetAuditStorageConfiguration() as FileSystemAuditOptions),
                    "memory" => new MemoryAuditStorage(),
                    // Fail loud rather than silently swapping in an in-memory store: a "database"
                    // audit store would lose every audit record on process exit, defeating the
                    // purpose of an audit trail. Database-backed audit storage is not yet
                    // implemented, so refuse to start instead of pretending it works.
                    "database" => throw new NotSupportedException(
                        "Database audit storage ('PragmaticLogging:Audit:StorageType' = \"database\") is not yet " +
                        "implemented. Configure \"filesystem\" or \"memory\", or provide a custom " +
                        "Pragmatic.Logging.Privacy.Audit.IAuditStorage registration."),
                    _ => new FileSystemAuditStorage()
                };
            });

            services.TryAddSingleton<IAuditPolicy>(provider =>
            {
                var adapter = provider.GetRequiredService<OptionsAdapter>();
                var policyType = adapter.CurrentOptions.Audit.PolicyType;

                return policyType.ToLowerInvariant() switch
                {
                    "gdpr" => new GdprAuditPolicy(),
                    "highperformance" => new PerformanceAuditPolicy(),
                    "development" => new DefaultAuditPolicy(),
                    "default" => new DefaultAuditPolicy(),
                    _ => new DefaultAuditPolicy()
                };
            });

            services.TryAddSingleton<PragmaticAuditService>(provider =>
            {
                var adapter = provider.GetRequiredService<OptionsAdapter>();
                var auditOptions = adapter.ToAuditServiceOptions();
                var storage = provider.GetRequiredService<Privacy.Audit.IAuditStorage>();
                var policy = provider.GetRequiredService<IAuditPolicy>();
                var logger = provider.GetRequiredService<ILogger<PragmaticAuditService>>();

                return new PragmaticAuditService(storage, policy, auditOptions, logger);
            });

            // Add hosted service for audit service
            services.TryAddSingleton<IHostedService>(provider =>
                provider.GetRequiredService<PragmaticAuditService>());

            return services;
        }
    }
}

/// <summary>
/// Service for managing configuration updates and reactions.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Gets the current configuration options.
    /// </summary>
    PragmaticLoggingOptions CurrentOptions { get; }

    /// <summary>
    /// Event fired when configuration changes.
    /// </summary>
    event EventHandler<OptionsChangedEventArgs> OptionsChanged;

    /// <summary>
    /// Updates the configuration and notifies subscribers.
    /// </summary>
    /// <param name="configure">Configuration update action</param>
    void UpdateConfiguration(Action<PragmaticLoggingOptions> configure);
}

/// <summary>
/// Implementation of IConfigurationService that manages configuration updates.
/// </summary>
internal sealed class ConfigurationService : IConfigurationService, IDisposable
{
    private readonly OptionsAdapter _adapter;
    private readonly IDisposable _changeListener;

    public ConfigurationService(OptionsAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

        // Forward adapter events
        _changeListener = Disposable.Create(() => _adapter.OptionsChanged -= OnAdapterOptionsChanged);
        _adapter.OptionsChanged += OnAdapterOptionsChanged;
    }

    private readonly object _updateLock = new();

    public PragmaticLoggingOptions CurrentOptions => _adapter.CurrentOptions;

    public event EventHandler<OptionsChangedEventArgs>? OptionsChanged;

    public void UpdateConfiguration(Action<PragmaticLoggingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // IOptionsMonitor is read-only and cannot be written back to its source. We therefore
        // apply the mutation to the in-memory options instance the monitor currently exposes
        // (a shared, mutable POCO) and notify subscribers. Changes that originate from the
        // underlying IConfiguration source still flow through OnAdapterOptionsChanged.
        OptionsChangedEventArgs args;
        lock (_updateLock)
        {
            var current = _adapter.CurrentOptions;
            configure(current);
            args = new OptionsChangedEventArgs(current, current);
        }

        OptionsChanged?.Invoke(this, args);
    }

    public void Dispose()
    {
        _changeListener?.Dispose();
    }

    private void OnAdapterOptionsChanged(object? sender, OptionsChangedEventArgs e)
    {
        OptionsChanged?.Invoke(this, e);
    }
}

/// <summary>
/// Utility class for creating disposable resources.
/// </summary>
internal static class Disposable
{
    public static IDisposable Create(Action dispose)
    {
        return new AnonymousDisposable(dispose);
    }

    private sealed class AnonymousDisposable(Action dispose) : IDisposable
    {
        private readonly Action _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        private volatile bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _dispose();
            }
        }
    }
}