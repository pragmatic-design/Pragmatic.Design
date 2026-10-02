using Microsoft.Extensions.Hosting;

namespace Pragmatic.Logging.Configuration;

/// <summary>
/// Advanced preset manager with automatic environment detection and intelligent defaults.
/// </summary>
public static class PresetManager
{
    /// <summary>
    /// Applies the most appropriate preset based on environment and context.
    /// </summary>
    /// <param name="options">Options to configure</param>
    /// <param name="environment">Host environment information</param>
    /// <param name="context">Additional context for preset selection</param>
    public static void ApplySmartPreset(
        PragmaticLoggingOptions options,
        IHostEnvironment? environment = null,
        PresetContext? context = null)
    {
        var selectedPreset = DetermineOptimalPreset(environment, context);
        OptionsAdapter.ApplyConfigurationPreset(options, selectedPreset);

        // Apply context-specific overrides
        ApplyContextualOverrides(options, context);
    }

    /// <summary>
    /// Determines the optimal preset based on environment and context.
    /// </summary>
    /// <param name="environment">Host environment</param>
    /// <param name="context">Additional context</param>
    /// <returns>The recommended preset name</returns>
    public static string DetermineOptimalPreset(IHostEnvironment? environment, PresetContext? context)
    {
        // Context-based overrides take priority
        if (!string.IsNullOrEmpty(context?.PreferredPreset))
        {
            return context.PreferredPreset;
        }

        // Performance requirements override environment
        if (context?.RequiresHighPerformance == true)
        {
            return "HighPerformance";
        }

        // Security requirements override environment
        if (context?.RequiresMaxSecurity == true)
        {
            return "Security";
        }

        // Environment-based selection
        if (environment != null)
        {
            if (environment.IsDevelopment())
                return "Development";
            if (environment.IsStaging())
                return "Staging";
            if (environment.IsProduction())
                return "Production";
        }

        // Fallback to environment variable detection
        var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                     ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                     ?? Environment.GetEnvironmentVariable("ENVIRONMENT");

        return envName?.ToLowerInvariant() switch
        {
            "development" or "dev" => "Development",
            "staging" or "stage" => "Staging",
            "production" or "prod" => "Production",
            _ => "Production" // Safe default
        };
    }

    /// <summary>
    /// Creates a custom preset for microservices.
    /// </summary>
    /// <param name="options">Options to configure</param>
    /// <param name="serviceName">Name of the microservice</param>
    /// <param name="isDistributedSystem">Whether this is part of a distributed system</param>
    public static void ApplyMicroservicePreset(
        PragmaticLoggingOptions options,
        string serviceName,
        bool isDistributedSystem = true)
    {
        // Start with production preset as base
        OptionsAdapter.ApplyConfigurationPreset(options, "Production");

        // Microservice-specific overrides
        options.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Information; // More verbose for debugging
        options.EnableTelemetry = true; // Essential for distributed systems

        // Enhanced context for distributed tracing
        options.Context.IncludeCorrelationId = true;
        options.Context.IncludeRequestContext = isDistributedSystem;
        options.Context.IncludeMachineContext = true;
        options.Context.CustomProperties["ServiceName"] = serviceName;
        options.Context.CustomProperties["ServiceType"] = "Microservice";

        if (isDistributedSystem)
        {
            options.Context.CustomProperties["DistributedSystem"] = "true";
            options.Performance.BufferSize = 5000; // Larger buffers for high throughput
            options.RateLimiting.MaxMessagesPerSecond = 2000; // Higher limits for service-to-service communication
        }

        // Structured logging for better observability
        options.Providers.Json.Enabled = true;
        options.Providers.Json.WriteIndented = false; // Compact for production
        options.Providers.Console.Enabled = false; // Disable console in containerized environments
        options.Providers.File.Enabled = true; // Enable file logging for persistence

        // Enhanced audit for compliance
        options.Audit.Enabled = true;
        options.Audit.BatchSize = 200; // Moderate batch size
        options.Privacy.EnableSecretDetection = true; // Important for API keys, etc.
    }

    /// <summary>
    /// Creates a custom preset for web applications.
    /// </summary>
    /// <param name="options">Options to configure</param>
    /// <param name="isApiOnly">Whether this is an API-only application</param>
    /// <param name="hasUserData">Whether the application processes user data</param>
    public static void ApplyWebApplicationPreset(
        PragmaticLoggingOptions options,
        bool isApiOnly = false,
        bool hasUserData = true)
    {
        // Start with production preset
        OptionsAdapter.ApplyConfigurationPreset(options, "Production");

        // Web-specific adjustments
        options.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Information;

        // Enhanced context for web requests
        options.Context.IncludeRequestContext = true;
        options.Context.IncludeUserContext = hasUserData;
        options.Context.CustomProperties["ApplicationType"] = isApiOnly ? "WebAPI" : "WebApplication";

        if (hasUserData)
        {
            // Enhanced privacy for user data
            options.Privacy.EnableRedaction = true;
            options.Privacy.EnableSecretDetection = true;
            options.Privacy.ComplianceStandard = "GDPR";

            // More aggressive secret detection
            options.Privacy.SecretDetection.MinimumConfidenceForRedaction = 0.6;

            // Comprehensive audit trail
            options.Audit.Enabled = true;
            options.Audit.PolicyType = "GDPR";
        }

        // Rate limiting for web traffic
        options.RateLimiting.Enabled = true;
        options.RateLimiting.MaxMessagesPerSecond = 1500;
        options.RateLimiting.Strategy = "SlidingWindow"; // Better for web traffic patterns

        // Balanced performance for web workloads
        options.Performance.UseBackgroundProcessing = true;
        options.Performance.BufferSize = 2000;
    }

    /// <summary>
    /// Creates a custom preset for background services and workers.
    /// </summary>
    /// <param name="options">Options to configure</param>
    /// <param name="isLongRunning">Whether this is a long-running background service</param>
    /// <param name="processesLargeVolumes">Whether it processes large volumes of data</param>
    public static void ApplyBackgroundServicePreset(
        PragmaticLoggingOptions options,
        bool isLongRunning = true,
        bool processesLargeVolumes = false)
    {
        // Start with appropriate base preset
        var basePreset = processesLargeVolumes ? "HighPerformance" : "Production";
        OptionsAdapter.ApplyConfigurationPreset(options, basePreset);

        // Background service specific adjustments
        options.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Information;
        options.Context.CustomProperties["ApplicationType"] = "BackgroundService";
        options.Context.IncludeRequestContext = false; // No HTTP requests
        options.Context.IncludeMachineContext = true; // Important for resource monitoring

        if (isLongRunning)
        {
            // Optimizations for long-running processes
            options.Performance.UseBackgroundProcessing = true;
            options.Performance.BufferSize = processesLargeVolumes ? 10000 : 3000;
            options.Audit.BatchSize = processesLargeVolumes ? 500 : 150;

            // File logging for persistence across restarts
            options.Providers.File.Enabled = true;
            options.Providers.File.MaxFileSizeMB = processesLargeVolumes ? 500 : 200;
            options.Providers.Console.Enabled = false; // Usually no console in background services
        }

        if (processesLargeVolumes)
        {
            // High-performance settings for data processing
            options.RateLimiting.MaxMessagesPerSecond = 10000;
            options.Performance.BackgroundQueueSize = 50000;
            options.Privacy.SecretDetection.MaxPatternsPerScan = 20; // Limit patterns for performance
        }
    }

    /// <summary>
    /// Creates a preset for local development and testing.
    /// </summary>
    /// <param name="options">Options to configure</param>
    /// <param name="enableVerboseDebugging">Whether to enable verbose debugging</param>
    public static void ApplyDevelopmentPreset(
        PragmaticLoggingOptions options,
        bool enableVerboseDebugging = false)
    {
        OptionsAdapter.ApplyConfigurationPreset(options, "Development");

        if (enableVerboseDebugging)
        {
            options.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Trace; // Most verbose
            options.Privacy.EnableRedaction = false; // See all data in development
            options.Privacy.EnableSecretDetection = false; // Don't redact test secrets
            options.Providers.Console.UseColors = true;
            options.Providers.Json.WriteIndented = true; // Readable JSON

            // Detailed context for debugging
            options.Context.IncludeUserContext = true;
            options.Context.IncludeRequestContext = true;
            options.Context.IncludeMachineContext = true;
            options.Context.CustomProperties["DevelopmentMode"] = "Verbose";
        }
    }

    private static void ApplyContextualOverrides(PragmaticLoggingOptions options, PresetContext? context)
    {
        if (context == null)
            return;

        // Apply specific overrides based on context
        if (context.MinimumLogLevel.HasValue)
        {
            options.MinimumLevel = context.MinimumLogLevel.Value;
        }

        if (context.ForceEnableAudit)
        {
            options.Audit.Enabled = true;
        }

        if (context.ForceDisableRedaction)
        {
            options.Privacy.EnableRedaction = false;
            options.Privacy.EnableSecretDetection = false;
        }

        if (context.CustomProperties?.Count > 0)
        {
            foreach (var kvp in context.CustomProperties)
            {
                options.Context.CustomProperties[kvp.Key] = kvp.Value;
            }
        }

        if (!string.IsNullOrEmpty(context.ComplianceStandard))
        {
            options.Privacy.ComplianceStandard = context.ComplianceStandard;
        }
    }
}

/// <summary>
/// Context information for intelligent preset selection.
/// </summary>
public sealed class PresetContext
{
    /// <summary>Gets or sets the preferred preset name to override automatic detection.</summary>
    public string? PreferredPreset { get; set; }

    /// <summary>Gets or sets whether high performance is required.</summary>
    public bool RequiresHighPerformance { get; set; }

    /// <summary>Gets or sets whether maximum security is required.</summary>
    public bool RequiresMaxSecurity { get; set; }

    /// <summary>Gets or sets the minimum log level override.</summary>
    public Microsoft.Extensions.Logging.LogLevel? MinimumLogLevel { get; set; }

    /// <summary>Gets or sets whether to force enable audit trail.</summary>
    public bool ForceEnableAudit { get; set; }

    /// <summary>Gets or sets whether to force disable redaction (for testing).</summary>
    public bool ForceDisableRedaction { get; set; }

    /// <summary>Gets or sets custom properties to add to context.</summary>
    public Dictionary<string, string>? CustomProperties { get; set; }

    /// <summary>Gets or sets the compliance standard override.</summary>
    public string? ComplianceStandard { get; set; }

    /// <summary>Gets or sets whether this is a containerized environment.</summary>
    public bool IsContainerized { get; set; }

    /// <summary>Gets or sets whether this is a cloud environment.</summary>
    public bool IsCloudEnvironment { get; set; }

    /// <summary>Gets or sets the expected message throughput (messages/second).</summary>
    public int? ExpectedThroughput { get; set; }
}