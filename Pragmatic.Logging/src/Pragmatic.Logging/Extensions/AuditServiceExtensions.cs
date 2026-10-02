using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Logging.Privacy.Audit;
using Pragmatic.Logging.Privacy.Audit.Storage;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Extensions;

/// <summary>
/// Extension methods for configuring audit services.
/// </summary>
public static class AuditServiceExtensions
{
    /// <summary>
    /// Adds the Pragmatic Audit Service to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPragmaticAudit(
        this IServiceCollection services,
        Action<AuditServiceBuilder>? configure = null)
    {
        var builder = new AuditServiceBuilder(services);

        // Default configuration
        builder.UseFileSystemStorage()
               .UseDefaultPolicy()
               .WithOptions(new AuditServiceOptions());

        // Apply custom configuration
        configure?.Invoke(builder);

        // Register the main service
        services.TryAddSingleton<PragmaticAuditService>();
        services.AddHostedService<PragmaticAuditService>(provider =>
            provider.GetRequiredService<PragmaticAuditService>());

        return services;
    }
}

/// <summary>
/// Builder for configuring audit services.
/// </summary>
public sealed class AuditServiceBuilder
{
    private readonly IServiceCollection _services;

    internal AuditServiceBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Configures file system storage for audit entries.
    /// </summary>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The builder for chaining</returns>
    public AuditServiceBuilder UseFileSystemStorage(Action<FileSystemAuditOptions>? configure = null)
    {
        _services.RemoveAll<IAuditStorage>();

        var options = new FileSystemAuditOptions();
        configure?.Invoke(options);

        _services.AddSingleton<IAuditStorage>(provider => new FileSystemAuditStorage(options));

        return this;
    }

    /// <summary>
    /// Configures a custom storage implementation for audit entries.
    /// </summary>
    /// <typeparam name="TStorage">The storage implementation type</typeparam>
    /// <returns>The builder for chaining</returns>
    public AuditServiceBuilder UseStorage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStorage>()
        where TStorage : class, IAuditStorage
    {
        _services.RemoveAll<IAuditStorage>();
        _services.AddSingleton<IAuditStorage, TStorage>();

        return this;
    }

    /// <summary>
    /// Configures a custom storage implementation using a factory.
    /// </summary>
    /// <param name="factory">The factory function</param>
    /// <returns>The builder for chaining</returns>
    public AuditServiceBuilder UseStorage(Func<IServiceProvider, IAuditStorage> factory)
    {
        _services.RemoveAll<IAuditStorage>();
        _services.AddSingleton(factory);

        return this;
    }

    /// <summary>
    /// Configures the default audit policy.
    /// </summary>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The builder for chaining</returns>
    public AuditServiceBuilder UseDefaultPolicy(Action<AuditPolicyOptions>? configure = null)
    {
        _services.RemoveAll<IAuditPolicy>();

        var options = new AuditPolicyOptions();
        configure?.Invoke(options);

        _services.AddSingleton<IAuditPolicy>(provider => new DefaultAuditPolicy(options));

        return this;
    }

    /// <summary>
    /// Configures GDPR-specific audit policy.
    /// </summary>
    /// <returns>The builder for chaining</returns>
    public AuditServiceBuilder UseGdprPolicy()
    {
        _services.RemoveAll<IAuditPolicy>();
        _services.AddSingleton<IAuditPolicy, GdprAuditPolicy>();

        return this;
    }

    /// <summary>
    /// Configures performance-focused audit policy.
    /// </summary>
    /// <returns>The builder for chaining</returns>
    public AuditServiceBuilder UsePerformancePolicy()
    {
        _services.RemoveAll<IAuditPolicy>();
        _services.AddSingleton<IAuditPolicy, PerformanceAuditPolicy>();

        return this;
    }

    /// <summary>
    /// Configures a custom audit policy.
    /// </summary>
    /// <typeparam name="TPolicy">The policy implementation type</typeparam>
    /// <returns>The builder for chaining</returns>
    public AuditServiceBuilder UsePolicy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPolicy>()
        where TPolicy : class, IAuditPolicy
    {
        _services.RemoveAll<IAuditPolicy>();
        _services.AddSingleton<IAuditPolicy, TPolicy>();

        return this;
    }

    /// <summary>
    /// Configures audit service options.
    /// </summary>
    /// <param name="options">The options to use</param>
    /// <returns>The builder for chaining</returns>
    public AuditServiceBuilder WithOptions(AuditServiceOptions options)
    {
        _services.RemoveAll<AuditServiceOptions>();
        _services.AddSingleton(options);

        return this;
    }

    /// <summary>
    /// Configures audit service options using a configuration action.
    /// </summary>
    /// <param name="configure">The configuration action</param>
    /// <returns>The builder for chaining</returns>
    public AuditServiceBuilder WithOptions(Action<AuditServiceOptions> configure)
    {
        var options = new AuditServiceOptions();
        configure(options);

        return WithOptions(options);
    }
}

/// <summary>
/// Extension methods for integrating audit with logging configuration.
/// </summary>
public static class LoggingAuditExtensions
{
    /// <param name="config">The provider configuration</param>
    extension(PragmaticProviderConfiguration config)
    {
        /// <summary>
        /// Enables audit trail with default file system storage.
        /// </summary>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration EnableAuditTrail()
        {
            config.Privacy.EnableAuditTrail = true;
            return config;
        }

        /// <summary>
        /// Enables audit trail with custom storage and policy.
        /// </summary>
        /// <param name="configureAudit">Audit configuration action</param>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration EnableAuditTrail(Action<AuditServiceBuilder> configureAudit)
        {
            config.Privacy.EnableAuditTrail = true;

            // Store the configuration action for later use during DI setup
            config.CustomProperties["AuditConfiguration"] = configureAudit;

            return config;
        }
    }
}

/// <summary>
/// Pre-configured audit setups for common scenarios.
/// </summary>
public static class AuditPresets
{
    /// <param name="builder">The audit service builder</param>
    extension(AuditServiceBuilder builder)
    {
        /// <summary>
        /// Configures audit for development environments.
        /// </summary>
        /// <returns>The configured builder</returns>
        public AuditServiceBuilder ForDevelopment()
        {
            return builder
                .UseFileSystemStorage(options =>
                {
                    options.BasePath = "./dev-audit-logs";
                    options.CompressOldFiles = false;
                })
                .UsePerformancePolicy() // Only critical events
                .WithOptions(options =>
                {
                    options.BatchSize = 10;
                    options.FlushInterval = TimeSpan.FromSeconds(5);
                });
        }

        /// <summary>
        /// Configures audit for production environments with GDPR compliance.
        /// </summary>
        /// <returns>The configured builder</returns>
        public AuditServiceBuilder ForProduction()
        {
            return builder
                .UseFileSystemStorage(options =>
                {
                    options.BasePath = "/var/log/audit";
                    options.CompressOldFiles = true;
                    options.CompressionAge = TimeSpan.FromDays(1);
                })
                .UseGdprPolicy()
                .WithOptions(options =>
                {
                    options.BatchSize = 500;
                    options.FlushInterval = TimeSpan.FromSeconds(30);
                    options.EnableCompression = true;
                });
        }

        /// <summary>
        /// Configures audit for high-volume environments with performance focus.
        /// </summary>
        /// <returns>The configured builder</returns>
        public AuditServiceBuilder ForHighVolume()
        {
            return builder
                .UseFileSystemStorage(options =>
                {
                    options.BasePath = "/fast-storage/audit";
                    options.CompressOldFiles = true;
                    options.CompressionAge = TimeSpan.FromHours(6);
                    options.MaxFileSizeMB = 500;
                })
                .UsePerformancePolicy()
                .WithOptions(options =>
                {
                    options.BatchSize = 1000;
                    options.FlushInterval = TimeSpan.FromSeconds(10);
                    options.MaxQueueSize = 50000;
                    options.EnableCompression = true;
                    options.EnableDeduplication = true;
                });
        }

        /// <summary>
        /// Configures audit for compliance-focused environments.
        /// </summary>
        /// <returns>The configured builder</returns>
        public AuditServiceBuilder ForCompliance()
        {
            return builder
                .UseFileSystemStorage(options =>
                {
                    options.BasePath = "/compliance/audit-logs";
                    options.CompressOldFiles = false; // Keep uncompressed for compliance tools
                })
                .UseGdprPolicy()
                .WithOptions(options =>
                {
                    options.BatchSize = 100;
                    options.FlushInterval = TimeSpan.FromSeconds(5); // Fast flush for compliance
                    options.EnableCompression = false;
                });
        }
    }
}