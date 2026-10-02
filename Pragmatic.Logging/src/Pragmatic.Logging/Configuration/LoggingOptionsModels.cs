namespace Pragmatic.Logging.Configuration;


/// <summary>
/// Privacy and data protection configuration options.
/// </summary>
public sealed class PrivacyOptions
{
    /// <summary>
    ///     Gets or sets whether the <b>heuristic</b> redaction is enabled — the property-name and
    ///     message patterns.
    /// </summary>
    /// <remarks>
    ///     It does not govern declared redaction. A member marked <c>[NotLogged]</c> or
    ///     <c>[PersonalData]</c> is redacted in every environment, with no way to switch it off,
    ///     because a declaration is a contract and "never in the logs" has no environment qualifier.
    ///     What this flag turns off is the guessing: patterns have false positives and cost
    ///     readability, which is a real reason to want them quiet in development and no reason at all
    ///     to stop honouring what someone wrote down.
    /// </remarks>
    public bool EnableRedaction { get; set; } = true;

    /// <summary>
    /// Gets or sets whether secret detection is enabled.
    /// </summary>
    public bool EnableSecretDetection { get; set; } = true;

    /// <summary>
    /// Gets or sets the compliance standard to follow.
    /// Valid values: General, GDPR, HIPAA, PCIDSS.
    /// </summary>
    public string ComplianceStandard { get; set; } = "General";

    /// <summary>
    /// Gets or sets secret detection options.
    /// </summary>
    public SecretDetectionConfigOptions SecretDetection { get; set; } = new();

    /// <summary>
    /// Gets or sets data redaction options.
    /// </summary>
    public DataRedactionConfigOptions DataRedaction { get; set; } = new();
}

/// <summary>
/// Secret detection configuration options for IConfiguration binding.
/// </summary>
public sealed class SecretDetectionConfigOptions
{
    /// <summary>Gets or sets the minimum length for a potential secret.</summary>
    public int MinimumSecretLength { get; set; } = 8;

    /// <summary>Gets or sets the maximum length for a potential secret.</summary>
    public int MaximumSecretLength { get; set; } = 2048;

    /// <summary>Gets or sets whether to precompile critical patterns for performance.</summary>
    public bool PrecompilePatterns { get; set; } = true;

    /// <summary>Gets or sets the minimum confidence threshold for redaction (0.0-1.0).</summary>
    public double MinimumConfidenceForRedaction { get; set; } = 0.7;

    /// <summary>Gets or sets the redaction style.</summary>
    public string RedactionStyle { get; set; } = "Placeholder";

    /// <summary>Gets or sets whether to enable audit trail for detections.</summary>
    public bool EnableAuditTrail { get; set; } = true;

    /// <summary>Gets or sets the maximum number of patterns to scan per operation for performance.</summary>
    public int MaxPatternsPerScan { get; set; } = 50;
}

/// <summary>
/// Data redaction configuration options for IConfiguration binding.
/// </summary>
public sealed class DataRedactionConfigOptions
{
    /// <summary>Gets or sets the redaction placeholder text.</summary>
    public string RedactionPlaceholder { get; set; } = "[REDACTED]";

    /// <summary>Gets or sets whether to preserve the length of redacted strings.</summary>
    public bool PreserveLengths { get; set; }

    /// <summary>Gets or sets whether to preserve JSON structure when redacting.</summary>
    public bool PreserveJsonStructure { get; set; } = true;

    /// <summary>Gets or sets sensitive property names.</summary>
    public string[] SensitivePropertyNames { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets regex patterns for property names that should be redacted.</summary>
    public string[] PropertyNamePatterns { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets regex patterns for redacting content in log messages.</summary>
    public string[] MessageRedactionPatterns { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Audit trail configuration options.
/// </summary>
public sealed class AuditOptions
{
    /// <summary>
    /// Gets or sets whether audit trail is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the batch size for audit entries.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the flush interval in seconds.
    /// </summary>
    public int FlushIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets whether stored audit entries should be compressed.
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Gets or sets the storage type for audit entries.
    /// Valid values: FileSystem, Database, Memory.
    /// </summary>
    public string StorageType { get; set; } = "FileSystem";

    /// <summary>
    /// Gets or sets the audit policy type.
    /// Valid values: Default, GDPR, HighPerformance, Development.
    /// </summary>
    public string PolicyType { get; set; } = "Default";

    /// <summary>
    /// Gets or sets storage-specific options.
    /// </summary>
    public Dictionary<string, object> StorageOptions { get; set; } = new();
}

/// <summary>
/// Provider-specific configuration options.
/// </summary>
public sealed class ProvidersOptions : IEnumerable<KeyValuePair<string, object>>
{
    /// <summary>
    /// Gets or sets JSON provider options.
    /// </summary>
    public JsonProviderOptions Json { get; set; } = new();

    /// <summary>
    /// Gets or sets console provider options.
    /// </summary>
    public ConsoleProviderOptions Console { get; set; } = new();

    /// <summary>
    /// Gets or sets file provider options.
    /// </summary>
    public FileProviderOptions File { get; set; } = new();

    /// <summary>
    /// Gets the count of enabled providers.
    /// </summary>
    public int Count => GetEnabledProviders().Count();

    /// <summary>
    /// Gets all enabled providers.
    /// </summary>
    /// <returns>Enumerable of enabled provider configurations</returns>
    public IEnumerable<KeyValuePair<string, object>> GetEnabledProviders()
    {
        if (Json.Enabled)
            yield return new KeyValuePair<string, object>("Json", Json);
        if (Console.Enabled)
            yield return new KeyValuePair<string, object>("Console", Console);
        if (File.Enabled)
            yield return new KeyValuePair<string, object>("File", File);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the enabled providers.
    /// </summary>
    /// <returns>Enumerator for enabled providers</returns>
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
    {
        return GetEnabledProviders().GetEnumerator();
    }

    /// <summary>
    /// Returns an enumerator that iterates through the enabled providers.
    /// </summary>
    /// <returns>Enumerator for enabled providers</returns>
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

/// <summary>
/// JSON provider configuration options.
/// </summary>
public sealed class JsonProviderOptions
{
    /// <summary>Gets or sets whether JSON provider is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether to write indented JSON.</summary>
    public bool WriteIndented { get; set; }

    /// <summary>Gets or sets the JSON naming policy.</summary>
    public string NamingPolicy { get; set; } = "CamelCase";

    /// <summary>Gets or sets whether to include timestamps.</summary>
    public bool IncludeTimestamps { get; set; } = true;

    /// <summary>Gets or sets the timestamp format.</summary>
    public string TimestampFormat { get; set; } = "yyyy-MM-ddTHH:mm:ss.fffZ";
}

/// <summary>
/// Console provider configuration options.
/// </summary>
public sealed class ConsoleProviderOptions
{
    /// <summary>Gets or sets whether console provider is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether to use colors.</summary>
    public bool UseColors { get; set; } = true;

    /// <summary>Gets or sets whether to include structured data.</summary>
    public bool IncludeStructuredData { get; set; } = true;

    /// <summary>Gets or sets the output format.</summary>
    public string OutputFormat { get; set; } = "Structured";
}

/// <summary>
/// File provider configuration options.
/// </summary>
public sealed class FileProviderOptions
{
    /// <summary>Gets or sets whether file provider is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the base path for log files.</summary>
    public string BasePath { get; set; } = "./logs";

    /// <summary>Gets or sets the file name pattern.</summary>
    public string FileNamePattern { get; set; } = "app-{Date}.log";

    /// <summary>Gets or sets the maximum file size in MB.</summary>
    public int MaxFileSizeMB { get; set; } = 100;

    /// <summary>Gets or sets whether to compress old files.</summary>
    public bool CompressOldFiles { get; set; } = true;

    /// <summary>Gets or sets the retention period in days.</summary>
    public int RetentionDays { get; set; } = 30;
}

/// <summary>
/// Performance optimization configuration options.
/// </summary>
public sealed class PerformanceOptions
{
    /// <summary>
    /// Gets or sets the buffer size for batching operations.
    /// </summary>
    public int BufferSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the flush threshold for automatic flushing.
    /// </summary>
    public int FlushThreshold { get; set; } = 100;

    /// <summary>
    /// Gets or sets whether to enable zero-allocation optimizations.
    /// </summary>
    public bool EnableZeroAllocation { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to use background processing.
    /// </summary>
    public bool UseBackgroundProcessing { get; set; } = true;

    /// <summary>
    /// Gets or sets the background processing queue size.
    /// </summary>
    public int BackgroundQueueSize { get; set; } = 10000;
}

/// <summary>
/// Context enrichment configuration options.
/// </summary>
public sealed class ContextOptions
{
    /// <summary>
    /// Gets or sets whether to include correlation IDs.
    /// </summary>
    public bool IncludeCorrelationId { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include user context.
    /// </summary>
    public bool IncludeUserContext { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include request context.
    /// </summary>
    public bool IncludeRequestContext { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include machine context.
    /// </summary>
    public bool IncludeMachineContext { get; set; } = true;

    /// <summary>
    /// Gets or sets custom context properties to include.
    /// </summary>
    public Dictionary<string, string> CustomProperties { get; set; } = new();
}

/// <summary>
/// Console color modes for console output.
/// </summary>
public enum ConsoleColorMode
{
    /// <summary>Automatically detect color support.</summary>
    Auto = 0,
    /// <summary>Never use colors.</summary>
    Never = 1,
    /// <summary>Always use colors.</summary>
    Always = 2
}

/// <summary>
/// Batching configuration options for performance optimization.
/// </summary>
public sealed class BatchingOptions
{
    /// <summary>Gets or sets the maximum number of log entries per batch.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Gets or sets the maximum time to wait before flushing a batch.</summary>
    public TimeSpan FlushTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets whether to use background processing for batches.</summary>
    public bool UseBackgroundProcessing { get; set; } = true;

    /// <summary>Gets or sets the maximum queue size for background processing.</summary>
    public int MaxQueueSize { get; set; } = 10000;
}

/// <summary>
/// Rate limiting configuration options.
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>
    /// Gets or sets whether rate limiting is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum messages per second.
    /// </summary>
    public int MaxMessagesPerSecond { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the burst size for rate limiting.
    /// </summary>
    public int BurstSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the rate limiting strategy.
    /// Valid values: TokenBucket, FixedWindow, SlidingWindow.
    /// </summary>
    public string Strategy { get; set; } = "TokenBucket";
}