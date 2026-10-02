using Microsoft.Extensions.Options;
using Pragmatic.Logging.Privacy;
using Pragmatic.Logging.Privacy.Audit;
using Pragmatic.Logging.Privacy.Audit.Storage;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Configuration;

/// <summary>
/// Adapter that converts PragmaticLoggingOptions to specific service configurations.
/// Supports IOptionsMonitor pattern for real-time configuration updates.
/// </summary>
public sealed class OptionsAdapter : IDisposable
{
    private readonly IOptionsMonitor<PragmaticLoggingOptions> _optionsMonitor;
    private readonly IDisposable? _changeListener;
    private volatile PragmaticLoggingOptions _currentOptions;

    public OptionsAdapter(IOptionsMonitor<PragmaticLoggingOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _currentOptions = _optionsMonitor.CurrentValue;

        // Listen for configuration changes
        _changeListener = _optionsMonitor.OnChange(OnOptionsChanged);
    }

    /// <summary>
    /// Gets the current options snapshot.
    /// </summary>
    public PragmaticLoggingOptions CurrentOptions => _currentOptions;

    /// <summary>
    /// Event fired when configuration options change.
    /// </summary>
    public event EventHandler<OptionsChangedEventArgs>? OptionsChanged;

    /// <summary>
    /// Converts to SecretDetectionOptions.
    /// </summary>
    /// <returns>Configured SecretDetectionOptions</returns>
    public SecretDetectionOptions ToSecretDetectionOptions()
    {
        var options = _currentOptions;
        var secretConfig = options.Privacy.SecretDetection;

        var result = new SecretDetectionOptions
        {
            MinimumSecretLength = secretConfig.MinimumSecretLength,
            MaximumSecretLength = secretConfig.MaximumSecretLength,
            PrecompilePatterns = secretConfig.PrecompilePatterns,
            MinimumConfidenceForRedaction = secretConfig.MinimumConfidenceForRedaction,
            EnableAuditTrail = secretConfig.EnableAuditTrail,
            RedactionStyle = ParseRedactionStyle(secretConfig.RedactionStyle)
        };

        ApplyPerformanceOptimizations(result, options.Performance);
        return result;
    }

    /// <summary>
    /// Converts to PragmaticDataRedactorConfiguration.
    /// </summary>
    /// <returns>Configured PragmaticDataRedactorConfiguration</returns>
    public PragmaticDataRedactorConfiguration ToDataRedactorConfiguration()
    {
        var options = _currentOptions;
        var redactionConfig = options.Privacy.DataRedaction;

        var result = new PragmaticDataRedactorConfiguration
        {
            RedactionPlaceholder = redactionConfig.RedactionPlaceholder,
            PreserveLengths = redactionConfig.PreserveLengths,
            PreserveJsonStructure = redactionConfig.PreserveJsonStructure,
            SensitivePropertyNames = redactionConfig.SensitivePropertyNames,
            PropertyNamePatterns = redactionConfig.PropertyNamePatterns,
            MessageRedactionPatterns = redactionConfig.MessageRedactionPatterns,
            // The audit switch, not the redaction one. Reading Privacy.EnableRedaction would make
            // turning off the pattern heuristic also turn off the redaction audit trail, and the
            // development preset would do both at once without saying so.
            EnableAuditTrail = options.Audit.Enabled,
            EnableSecretDetection = options.Privacy.EnableSecretDetection,
            ComplianceStandard = ParseComplianceStandard(options.Privacy.ComplianceStandard)
        };

        return result;
    }

    /// <summary>
    /// Converts to AuditServiceOptions.
    /// </summary>
    /// <returns>Configured AuditServiceOptions</returns>
    public AuditServiceOptions ToAuditServiceOptions()
    {
        var options = _currentOptions;
        var auditConfig = options.Audit;

        return new AuditServiceOptions
        {
            BatchSize = auditConfig.BatchSize,
            FlushInterval = TimeSpan.FromSeconds(auditConfig.FlushIntervalSeconds),
            MaxQueueSize = options.Performance.BackgroundQueueSize,
            EnableCompression = auditConfig.EnableCompression,
            EnableDeduplication = false // Can be configured later
        };
    }

    /// <summary>
    /// Converts to PragmaticProviderConfiguration based on provider type.
    /// </summary>
    /// <param name="providerType">The provider type</param>
    /// <returns>Configured PragmaticProviderConfiguration</returns>
    public PragmaticProviderConfiguration ToProviderConfiguration(string providerType)
    {
        var options = _currentOptions;

        return new PragmaticProviderConfiguration
        {
            EnableHighPerformance = options.HighPerformanceMode,
            MinimumLevel = options.MinimumLevel,
            BufferSize = options.Performance.BufferSize,
            FlushThreshold = options.Performance.FlushThreshold,
            UseBackgroundProcessing = options.Performance.UseBackgroundProcessing,
            IncludeCorrelationId = options.Context.IncludeCorrelationId,
            IncludeUserContext = options.Context.IncludeUserContext,
            IncludeRequestContext = options.Context.IncludeRequestContext,
            IncludeMachineContext = options.Context.IncludeMachineContext,
            EnableTelemetry = options.EnableTelemetry,
            EnableRateLimiting = options.RateLimiting.Enabled,
            RateLimitStrategy = ParseRateLimitStrategy(options.RateLimiting.Strategy),
            MaxMessagesPerSecond = options.RateLimiting.MaxMessagesPerSecond,
            RateLimitBurstSize = options.RateLimiting.BurstSize
        };
    }

    /// <summary>
    /// Gets configuration for a specific audit storage type.
    /// </summary>
    /// <returns>Storage configuration</returns>
    public object GetAuditStorageConfiguration()
    {
        var options = _currentOptions;
        var auditConfig = options.Audit;

        return auditConfig.StorageType.ToLowerInvariant() switch
        {
            "filesystem" => new FileSystemAuditOptions
            {
                BasePath = GetStorageOption("BasePath", "./audit-logs"),
                FilePattern = GetStorageOption("FilePattern", "audit-{0:yyyy-MM-dd}.jsonl"),
                CompressOldFiles = GetStorageOption("CompressOldFiles", true),
                CompressionAge = TimeSpan.FromDays(GetStorageOption("CompressionAgeDays", 7)),
                MaxFileSizeMB = GetStorageOption("MaxFileSizeMB", 100)
            },
            "memory" => new { /* Memory storage options if needed */ },
            "database" => new { /* Database storage options if needed */ },
            _ => new FileSystemAuditOptions() // Default fallback
        };

        T GetStorageOption<T>(string key, T defaultValue)
        {
            if (auditConfig.StorageOptions.TryGetValue(key, out var value) && value is T typedValue)
                return typedValue;
            return defaultValue;
        }
    }

    /// <summary>
    /// Applies a configuration preset to override individual settings.
    /// </summary>
    /// <param name="options">Options to modify</param>
    /// <param name="preset">Preset name</param>
    public static void ApplyConfigurationPreset(PragmaticLoggingOptions options, string? preset)
    {
        if (string.IsNullOrEmpty(preset))
            return;

        switch (preset.ToLowerInvariant())
        {
            case "development":
                ApplyDevelopmentPreset(options);
                break;
            case "staging":
                ApplyStagingPreset(options);
                break;
            case "production":
                ApplyProductionPreset(options);
                break;
            case "highperformance":
                ApplyHighPerformancePreset(options);
                break;
            case "security":
                ApplySecurityPreset(options);
                break;
        }
    }

    public void Dispose()
    {
        _changeListener?.Dispose();
    }

    private void OnOptionsChanged(PragmaticLoggingOptions newOptions)
    {
        // Runs on the IOptionsMonitor callback thread. Swap the snapshot atomically (the field is
        // volatile) and snapshot the delegate before invoking so a concurrent unsubscribe cannot
        // race the invocation. Isolate per-subscriber exceptions so one faulty handler does not
        // suppress the others or escape onto the monitor's callback thread.
        var previousOptions = _currentOptions;
        _currentOptions = newOptions;

        var handler = OptionsChanged;
        if (handler == null)
            return;

        var args = new OptionsChangedEventArgs(previousOptions, newOptions);
        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<OptionsChangedEventArgs>)subscriber)(this, args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[OptionsAdapter] OptionsChanged subscriber error: {ex}");
            }
        }
    }

    private static SecretRedactionStyle ParseRedactionStyle(string style)
    {
        return style.ToLowerInvariant() switch
        {
            "placeholder" => SecretRedactionStyle.Placeholder,
            "preservelength" => SecretRedactionStyle.PreserveLength,
            "preservestructure" => SecretRedactionStyle.PreserveStructure,
            "minimal" => SecretRedactionStyle.Minimal,
            _ => SecretRedactionStyle.Placeholder
        };
    }

    private static ComplianceStandard ParseComplianceStandard(string standard)
    {
        return standard.ToLowerInvariant() switch
        {
            "gdpr" => ComplianceStandard.Gdpr,
            "hipaa" => ComplianceStandard.Hipaa,
            "pcidss" => ComplianceStandard.PciDss,
            "general" => ComplianceStandard.General,
            _ => ComplianceStandard.General
        };
    }

    private static RateLimitStrategy ParseRateLimitStrategy(string strategy)
    {
        return strategy.ToLowerInvariant() switch
        {
            "tokenbucket" => RateLimitStrategy.TokenBucket,
            "fixedwindow" => RateLimitStrategy.FixedWindow,
            "slidingwindow" => RateLimitStrategy.SlidingWindow,
            _ => RateLimitStrategy.TokenBucket
        };
    }

    private static void ApplyPerformanceOptimizations(SecretDetectionOptions options, PerformanceOptions performance)
    {
        if (performance.EnableZeroAllocation)
        {
            options.PrecompilePatterns = true;
            options.RegexTimeoutSeconds = 1.0; // Shorter timeout for performance
        }

        if (!performance.UseBackgroundProcessing)
        {
            options.EnableAuditTrail = false; // Disable audit if not using background processing
        }
    }

    private static void ApplyDevelopmentPreset(PragmaticLoggingOptions options)
    {
        options.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Debug;
        // Turns off the pattern heuristic only. Declared redaction ([NotLogged], [PersonalData])
        // applies in development too — it is not routed through this flag.
        options.Privacy.EnableRedaction = false;
        options.Privacy.EnableSecretDetection = false;
        options.Audit.Enabled = false;
        options.Performance.UseBackgroundProcessing = false;
        options.RateLimiting.Enabled = false;
        options.Providers.Console.UseColors = true;
        options.Providers.Json.WriteIndented = true;
    }

    private static void ApplyStagingPreset(PragmaticLoggingOptions options)
    {
        options.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Information;
        options.Privacy.EnableRedaction = true;
        options.Privacy.EnableSecretDetection = true;
        options.Privacy.ComplianceStandard = "General";
        options.Audit.Enabled = true;
        options.Performance.UseBackgroundProcessing = true;
        options.RateLimiting.Enabled = true;
        options.RateLimiting.MaxMessagesPerSecond = 500;
    }

    private static void ApplyProductionPreset(PragmaticLoggingOptions options)
    {
        options.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Warning;
        options.Privacy.EnableRedaction = true;
        options.Privacy.EnableSecretDetection = true;
        options.Privacy.ComplianceStandard = "GDPR";
        options.Audit.Enabled = true;
        options.Performance.UseBackgroundProcessing = true;
        options.Performance.EnableZeroAllocation = true;
        options.RateLimiting.Enabled = true;
        options.RateLimiting.MaxMessagesPerSecond = 1000;
        options.Providers.Console.UseColors = false;
        options.Providers.Json.WriteIndented = false;
        options.Providers.File.Enabled = true;
    }

    private static void ApplyHighPerformancePreset(PragmaticLoggingOptions options)
    {
        options.HighPerformanceMode = true;
        options.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Error;
        options.Privacy.EnableRedaction = true;
        options.Privacy.EnableSecretDetection = true;
        options.Privacy.SecretDetection.PrecompilePatterns = true;
        options.Privacy.SecretDetection.MinimumConfidenceForRedaction = 0.9;
        options.Audit.Enabled = true;
        options.Audit.BatchSize = 1000;
        options.Performance.UseBackgroundProcessing = true;
        options.Performance.EnableZeroAllocation = true;
        options.Performance.BufferSize = 10000;
        options.RateLimiting.Enabled = true;
        options.RateLimiting.MaxMessagesPerSecond = 5000;
    }

    private static void ApplySecurityPreset(PragmaticLoggingOptions options)
    {
        options.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Information;
        options.Privacy.EnableRedaction = true;
        options.Privacy.EnableSecretDetection = true;
        options.Privacy.ComplianceStandard = "GDPR";
        options.Privacy.SecretDetection.MinimumSecretLength = 4;
        options.Privacy.SecretDetection.MinimumConfidenceForRedaction = 0.3;
        options.Privacy.DataRedaction.PreserveLengths = false;
        options.Privacy.DataRedaction.PreserveJsonStructure = false;
        options.Audit.Enabled = true;
        options.Audit.BatchSize = 50; // Smaller batches for security
        options.Performance.UseBackgroundProcessing = true;
        options.Context.IncludeUserContext = true;
        options.Context.IncludeRequestContext = true;
    }
}

/// <summary>
/// Event arguments for configuration changes.
/// </summary>
public sealed class OptionsChangedEventArgs(PragmaticLoggingOptions previousOptions, PragmaticLoggingOptions newOptions)
    : EventArgs
{
    /// <summary>
    /// Gets the previous configuration options.
    /// </summary>
    public PragmaticLoggingOptions PreviousOptions { get; } = previousOptions;

    /// <summary>
    /// Gets the new configuration options.
    /// </summary>
    public PragmaticLoggingOptions NewOptions { get; } = newOptions;
}