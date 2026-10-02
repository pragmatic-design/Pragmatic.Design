using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Pragmatic.Logging.Configuration;

namespace Pragmatic.Logging.Extensions;

/// <summary>
/// Extension methods for applying advanced configuration presets.
/// </summary>
public static class PresetExtensions
{
    /// <param name="services">The service collection</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds Pragmatic.Logging with automatic smart preset selection based on environment.
        /// </summary>
        /// <param name="environment">The host environment</param>
        /// <param name="context">Optional preset context</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLoggingWithSmartPreset(IHostEnvironment environment,
            PresetContext? context = null)
        {
            services.Configure<PragmaticLoggingOptions>(options =>
            {
                PresetManager.ApplySmartPreset(options, environment, context);
            });

            services.TryAddSingleton<OptionsAdapter>();
            services.AddPragmaticLoggingCore();

            return services;
        }

        /// <summary>
        /// Adds Pragmatic.Logging optimized for microservices.
        /// </summary>
        /// <param name="serviceName">Name of the microservice</param>
        /// <param name="isDistributedSystem">Whether this is part of a distributed system</param>
        /// <param name="additionalConfig">Additional configuration</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLoggingForMicroservice(string serviceName,
            bool isDistributedSystem = true,
            Action<PragmaticLoggingOptions>? additionalConfig = null)
        {
            services.Configure<PragmaticLoggingOptions>(options =>
            {
                PresetManager.ApplyMicroservicePreset(options, serviceName, isDistributedSystem);
                additionalConfig?.Invoke(options);
            });

            services.TryAddSingleton<OptionsAdapter>();
            services.AddPragmaticLoggingCore();

            return services;
        }

        /// <summary>
        /// Adds Pragmatic.Logging optimized for web applications.
        /// </summary>
        /// <param name="isApiOnly">Whether this is an API-only application</param>
        /// <param name="hasUserData">Whether the application processes user data</param>
        /// <param name="additionalConfig">Additional configuration</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLoggingForWebApplication(bool isApiOnly = false,
            bool hasUserData = true,
            Action<PragmaticLoggingOptions>? additionalConfig = null)
        {
            services.Configure<PragmaticLoggingOptions>(options =>
            {
                PresetManager.ApplyWebApplicationPreset(options, isApiOnly, hasUserData);
                additionalConfig?.Invoke(options);
            });

            services.TryAddSingleton<OptionsAdapter>();
            services.AddPragmaticLoggingCore();

            return services;
        }

        /// <summary>
        /// Adds Pragmatic.Logging optimized for background services and workers.
        /// </summary>
        /// <param name="isLongRunning">Whether this is a long-running background service</param>
        /// <param name="processesLargeVolumes">Whether it processes large volumes of data</param>
        /// <param name="additionalConfig">Additional configuration</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLoggingForBackgroundService(bool isLongRunning = true,
            bool processesLargeVolumes = false,
            Action<PragmaticLoggingOptions>? additionalConfig = null)
        {
            services.Configure<PragmaticLoggingOptions>(options =>
            {
                PresetManager.ApplyBackgroundServicePreset(options, isLongRunning, processesLargeVolumes);
                additionalConfig?.Invoke(options);
            });

            services.TryAddSingleton<OptionsAdapter>();
            services.AddPragmaticLoggingCore();

            return services;
        }

        /// <summary>
        /// Adds Pragmatic.Logging for local development with optional verbose debugging.
        /// </summary>
        /// <param name="enableVerboseDebugging">Whether to enable verbose debugging</param>
        /// <param name="additionalConfig">Additional configuration</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLoggingForDevelopment(bool enableVerboseDebugging = false,
            Action<PragmaticLoggingOptions>? additionalConfig = null)
        {
            services.Configure<PragmaticLoggingOptions>(options =>
            {
                PresetManager.ApplyDevelopmentPreset(options, enableVerboseDebugging);
                additionalConfig?.Invoke(options);
            });

            services.TryAddSingleton<OptionsAdapter>();
            services.AddPragmaticLoggingCore();

            return services;
        }

        /// <summary>
        /// Adds Pragmatic.Logging with cloud-optimized settings.
        /// </summary>
        /// <param name="cloudProvider">The cloud provider (AWS, Azure, GCP)</param>
        /// <param name="additionalConfig">Additional configuration</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLoggingForCloud(string cloudProvider,
            Action<PragmaticLoggingOptions>? additionalConfig = null)
        {
            services.Configure<PragmaticLoggingOptions>(options =>
            {
                // Start with production preset
                OptionsAdapter.ApplyConfigurationPreset(options, "Production");

                // Cloud-specific optimizations
                options.Context.CustomProperties["CloudProvider"] = cloudProvider;
                options.Context.CustomProperties["DeploymentType"] = "Cloud";
                options.Context.IncludeMachineContext = false; // Often not relevant in cloud

                // Optimize for cloud logging services
                options.Providers.Console.Enabled = false; // Cloud services capture stdout
                options.Providers.Json.Enabled = true; // Structured logs for cloud parsing
                options.Providers.File.Enabled = false; // Cloud handles log persistence

                // High throughput for cloud scalability
                options.Performance.BufferSize = 5000;
                options.RateLimiting.MaxMessagesPerSecond = 3000;

                // Cloud-specific provider settings based on cloud provider
                ApplyCloudProviderSpecificSettings(options, cloudProvider);

                additionalConfig?.Invoke(options);
            });

            services.TryAddSingleton<OptionsAdapter>();
            services.AddPragmaticLoggingCore();

            return services;
        }

        /// <summary>
        /// Adds Pragmatic.Logging with container-optimized settings.
        /// </summary>
        /// <param name="containerOrchestrator">The container orchestrator (Docker, Kubernetes, etc.)</param>
        /// <param name="additionalConfig">Additional configuration</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPragmaticLoggingForContainer(string containerOrchestrator = "Docker",
            Action<PragmaticLoggingOptions>? additionalConfig = null)
        {
            services.Configure<PragmaticLoggingOptions>(options =>
            {
                // Start with production preset
                OptionsAdapter.ApplyConfigurationPreset(options, "Production");

                // Container-specific optimizations
                options.Context.CustomProperties["ContainerOrchestrator"] = containerOrchestrator;
                options.Context.CustomProperties["DeploymentType"] = "Container";

                // Optimize for container logging
                options.Providers.Console.Enabled = true; // Containers capture stdout/stderr
                options.Providers.File.Enabled = false; // Avoid persistent storage in containers
                options.Providers.Json.Enabled = true; // Structured logs for log aggregation
                options.Providers.Json.WriteIndented = false; // Compact for log shipping

                // Performance optimizations for constrained resources
                options.Performance.BufferSize = 2000;
                options.Performance.UseBackgroundProcessing = true;
                options.Performance.EnableZeroAllocation = true;

                // Kubernetes-specific settings
                if (containerOrchestrator.Contains("Kubernetes", StringComparison.OrdinalIgnoreCase))
                {
                    options.Context.IncludeRequestContext = true; // Important for service mesh
                    options.Context.CustomProperties["Platform"] = "Kubernetes";
                    options.RateLimiting.MaxMessagesPerSecond = 2000; // Higher for service-to-service calls
                }

                additionalConfig?.Invoke(options);
            });

            services.TryAddSingleton<OptionsAdapter>();
            services.AddPragmaticLoggingCore();

            return services;
        }
    }

    private static void ApplyCloudProviderSpecificSettings(PragmaticLoggingOptions options, string cloudProvider)
    {
        switch (cloudProvider.ToLowerInvariant())
        {
            case "aws":
                options.Context.CustomProperties["LogGroup"] = "/aws/application";
                options.Privacy.SecretDetection.EnableAuditTrail = true; // Important for AWS compliance
                break;

            case "azure":
                options.Context.CustomProperties["WorkspaceId"] = "default";
                options.Audit.StorageType = "Database"; // Azure SQL/CosmosDB
                break;

            case "gcp":
            case "google":
                options.Context.CustomProperties["ProjectId"] = "default";
                options.Providers.Json.NamingPolicy = "CamelCase"; // GCP preference
                break;
        }
    }

    private static IServiceCollection AddPragmaticLoggingCore(this IServiceCollection services)
    {
        // Delegates to the canonical core registration in ConfigurationExtensions.
        return services.AddPragmaticLoggingWithOptions(_ => { });
    }
}