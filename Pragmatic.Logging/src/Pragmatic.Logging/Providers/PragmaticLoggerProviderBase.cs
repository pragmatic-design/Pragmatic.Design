using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Privacy;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Base class for Pragmatic.Logging providers that implements common functionality
/// like batching, configuration management, and metrics collection.
/// </summary>
public abstract partial class PragmaticLoggerProviderBase : IPragmaticLoggerProvider
{
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new();
    private readonly object _configLock = new();
    private volatile IPragmaticProviderConfiguration _configuration;
    private volatile bool _disposed;
    private volatile IDataRedactor? _dataRedactor;

    /// <summary>
    ///     Redacts the members a type declared with <c>[NotLogged]</c> or <c>[PersonalData]</c>.
    /// </summary>
    /// <remarks>
    ///     Set once by <c>AddPragmaticProvider</c>, which is the only place every provider passes
    ///     through. Deliberately NOT behind <c>Privacy.EnableRedaction</c>: that flag governs the
    ///     pattern heuristic, which has false positives and a readability cost worth turning off in
    ///     development. A member the developer marked is a contract, and it has no environment
    ///     qualifier.
    /// </remarks>
    public global::Pragmatic.Redaction.DeclaredRedactor? DeclaredRedactor { get; set; }

    /// <summary>
    ///     The application's JSON seam, whose metadata a complex structured value is serialized from.
    /// </summary>
    /// <remarks>
    ///     Set by <c>AddPragmaticProvider</c> beside <see cref="DeclaredRedactor" />. Without it the
    ///     provider uses a seam of its own, which knows the framework's types and, on a JIT runtime,
    ///     reflects; under Native AOT it knows only what was generated.
    /// </remarks>
    public global::Pragmatic.Serialization.PragmaticJsonOptions? JsonOptions { get; set; }

    private ComplexValueSerializer? _complexValues;

    /// <summary>
    ///     A complex structured value as JSON, written with <paramref name="providerOptions" /> from the
    ///     seam's metadata, or as its <c>ToString()</c> (counted) when the seam has none for its type.
    /// </summary>
    private protected string SerializeComplexValue(object value, System.Text.Json.JsonSerializerOptions providerOptions)
    {
        var serializer = _complexValues;
        if (serializer is null)
        {
            Interlocked.CompareExchange(ref _complexValues, new ComplexValueSerializer(providerOptions, JsonOptions), null);
            serializer = _complexValues;
        }

        return serializer.Serialize(value);
    }

    // Metrics tracking
    private long _totalMessages;
    private long _droppedMessages;
    private long _failedMessages;
    private long _redactedWithoutTemplate;

    // Per-name context filter decisions; replaced with the configuration. See ShouldIncludeContextProperty.
    private volatile ConcurrentDictionary<string, bool> _contextDecisions = new(StringComparer.Ordinal);

    // Fixed-size ring buffer for processing-time samples: zero allocation and O(1) per log call
    // (a ConcurrentQueue here allocated segments and its Count walk made every log call O(n)).
    private readonly double[] _processingTimes = new double[ProcessingTimeWindow];
    private int _processingTimeIndex;
    private const int ProcessingTimeWindow = 1024;

    private string? _lastError;
    private DateTime? _lastErrorTime;

    // Cache of compiled wildcard-pattern regexes, keyed by the original pattern string.
    // Avoids recompiling a Regex on every WriteLog call (hot path via IsEnabled / context filtering).
    private static readonly ConcurrentDictionary<string, Regex> PatternRegexCache = new();

    protected PragmaticLoggerProviderBase(string name, IPragmaticProviderConfiguration configuration)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        ValidateConfiguration();
        InitializeRedactor();
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IPragmaticProviderConfiguration Configuration => _configuration;

    /// <inheritdoc />
    public virtual ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name =>
            new PragmaticLogger(name, this));
    }


    /// <inheritdoc />
    public virtual void UpdateConfiguration(IPragmaticProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = configuration.Validate();
        if (errors.Any())
        {
            throw new ArgumentException($"Invalid configuration: {string.Join(", ", errors)}");
        }

        lock (_configLock)
        {
            var oldConfig = _configuration;
            _configuration = configuration;
            _contextDecisions = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

            try
            {
                OnConfigurationUpdated(oldConfig, configuration);
            }
            catch (Exception ex)
            {
                // Rollback on error
                _configuration = oldConfig;
                _contextDecisions = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
                RecordError($"Configuration update failed: {ex.Message}");
                throw;
            }
        }
    }

    /// <inheritdoc />
    public virtual ProviderMetrics GetMetrics()
    {
        // Average over the filled portion of the ring buffer.
        var sampleCount = (int)Math.Min(Interlocked.Read(ref _totalMessages), ProcessingTimeWindow);
        var avgProcessingTime = 0.0;
        if (sampleCount > 0)
        {
            var sum = 0.0;
            for (var i = 0; i < sampleCount; i++)
                sum += _processingTimes[i];
            avgProcessingTime = sum / sampleCount;
        }

        return new ProviderMetrics
        {
            TotalMessages = Interlocked.Read(ref _totalMessages),
            DroppedMessages = Interlocked.Read(ref _droppedMessages),
            FailedMessages = Interlocked.Read(ref _failedMessages),
            RedactedWithoutTemplate = Interlocked.Read(ref _redactedWithoutTemplate),
            ComplexValuesWithoutMetadata = _complexValues?.ValuesWithoutMetadata ?? 0,
            AverageProcessingTimeMs = avgProcessingTime,
            LastError = _lastError,
            LastErrorTime = _lastErrorTime,
            CustomMetrics = GetCustomMetrics()
        };
    }

    /// <inheritdoc />
    public virtual ProviderHealthStatus CheckHealth()
    {
        if (_disposed)
            return ProviderHealthStatus.Unhealthy;

        var metrics = GetMetrics();

        // Health logic based on error rates and queue status
        var totalMessages = metrics.TotalMessages;
        var failedMessages = metrics.FailedMessages;

        if (totalMessages > 0)
        {
            var errorRate = (double)failedMessages / totalMessages;

            if (errorRate > 0.1) // More than 10% errors
                return ProviderHealthStatus.Unhealthy;
            if (errorRate > 0.05) // More than 5% errors
                return ProviderHealthStatus.Degraded;
            if (errorRate > 0.01) // More than 1% errors
                return ProviderHealthStatus.Warning;
        }

        return PerformCustomHealthCheck();
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            DisposeCore();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Whether this provider can consume a log call directly from the caller's typed state,
    /// without materializing a <see cref="LogEntry" /> or rendering the message up front.
    /// Providers that write synchronously override this (the state must not escape the call,
    /// so async/queued providers must return false). Ambient scopes remain readable during the
    /// deferred call via <see cref="LoggerExternalScopeProvider.ForEachScope{TState}" /> —
    /// a provider that renders scope data must either enumerate them there or return false.
    /// </summary>
    protected internal virtual bool SupportsDeferredWrite => false;

    /// <summary>
    /// Attempts the deferred (non-materializing) write path. Returns false when any pipeline
    /// feature requires a materialized <see cref="LogEntry" /> — advanced filters, context
    /// enrichment, or redaction — in which case the caller falls back to the classic path.
    /// The caller has already checked <see cref="IsEnabled" />.
    /// </summary>
    internal bool TryWriteDeferred<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter,
        string category)
    {
        if (_disposed)
            return true; // swallow, same as WriteLog

        // A generated call site's state writes itself, with its masking already applied: declared
        // redaction has nothing left to do for it, so a non-empty declared redactor does not stop it the
        // way it stops the generic path below. Reached through the writer its type registered, so the
        // state is never boxed.
        if (SupportsUtf8State
            && CallSites.Utf8LogStateWriters<TState>.Writer is { } writer
            && CanWriteUtf8State(writer))
        {
            try
            {
                WriteUtf8State(logLevel, eventId, writer, in state, exception, category);
                Interlocked.Increment(ref _totalMessages);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedMessages);
                RecordError($"Failed to write log: {ex.Message}");
            }

            return true;
        }

        // Declared redaction needs the state's values before anything renders them, which the
        // deferred path would hand to the sink untouched.
        if (!SupportsDeferredWrite ||
            _dataRedactor != null ||
            DeclaredRedactor is { IsEmpty: false } ||
            _configuration.IncludeContextEnrichment ||
            _configuration.Filters.Filters.Count > 0)
        {
            return false;
        }

        try
        {
            WriteLogCoreDeferred(logLevel, eventId, state, exception, formatter, category);
            Interlocked.Increment(ref _totalMessages);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedMessages);
            RecordError($"Failed to write log: {ex.Message}");
        }

        return true;
    }

    /// <summary>
    /// Deferred write: consume the typed state directly. Only called when
    /// <see cref="SupportsDeferredWrite" /> is true and no pipeline feature needs a
    /// materialized entry. Render the message via <paramref name="formatter" /> only if
    /// the sink actually needs it. Note: processing-time samples are not recorded on this
    /// path (it exists precisely to avoid per-call bookkeeping).
    /// </summary>
    protected virtual void WriteLogCoreDeferred<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter,
        string category)
    {
    }

    /// <summary>
    /// Writes a log message using this provider.
    /// </summary>
    /// <param name="logEntry">The log entry to write</param>
    internal void WriteLog(LogEntry logEntry)
    {
        if (_disposed || !IsEnabled(logEntry.Category, logEntry.LogLevel))
            return;

        // Apply advanced filters if configured
        if (_configuration.Filters.Filters.Count > 0)
        {
            var filterChain = new LogFilterChain(_configuration.Filters.Filters);
            var filterContext = CreateFilterContext();

            if (!filterChain.ShouldLog(logEntry, filterContext))
            {
                Interlocked.Increment(ref _droppedMessages);
                return;
            }
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            // Enrich with context if enabled. Mode None asks for no context property: nothing to walk.
            if (_configuration.IncludeContextEnrichment && _configuration.ContextFilter.Mode != ContextFilterMode.None)
            {
                EnrichWithContext(logEntry);
            }

            // Filter structured properties if needed
            if (_configuration.IncludeStructuredProperties)
            {
                FilterStructuredProperties(logEntry);
            }

            // Declared redaction, unconditionally and before anything formats the entry. This is the
            // one place every provider passes through, which is the point: the JSON writers
            // serialize a complex property whole, the console writer Appends it and the debug writer
            // calls ToString(), and a record's ToString() prints every member. Redacting per provider
            // would leave the next provider leaking, silently.
            ApplyDeclaredRedaction(logEntry);

            // Apply the pattern heuristic if enabled
            if (_dataRedactor != null && logEntry.TryApplyRedaction(_dataRedactor, out var redactedEntry))
            {
                WriteLogCore(redactedEntry);
            }
            else
            {
                WriteLogCore(logEntry);
            }

            Interlocked.Increment(ref _totalMessages);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedMessages);
            RecordError($"Failed to write log: {ex.Message}");

            // Don't throw to prevent cascading failures
            OnWriteError(logEntry, ex);
        }
        finally
        {
            RecordProcessingTime(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }
    }

    /// <summary>
    /// Checks if logging is enabled for the specified category and level.
    /// </summary>
    /// <param name="categoryName">The category name</param>
    /// <param name="logLevel">The log level</param>
    /// <returns>True if enabled, false otherwise</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(string categoryName, LogLevel logLevel)
    {
        if (logLevel == LogLevel.None)
            return false;

        // Check category-specific levels first
        if (_configuration.CategoryLevels.TryGetValue(categoryName, out var categoryLevel))
        {
            return logLevel >= categoryLevel;
        }

        // Check wildcard patterns
        foreach (var kvp in _configuration.CategoryLevels)
        {
            if (IsPatternMatch(categoryName, kvp.Key))
            {
                return logLevel >= kvp.Value;
            }
        }

        // Fall back to minimum level
        return logLevel >= _configuration.MinimumLevel;
    }

    /// <summary>
    /// Core method for writing log entries. Must be implemented by derived classes.
    /// </summary>
    /// <param name="logEntry">The log entry to write</param>
    protected abstract void WriteLogCore(LogEntry logEntry);

    /// <summary>
    /// Called when configuration is updated. Override to handle configuration changes.
    /// </summary>
    /// <param name="oldConfig">The old configuration</param>
    /// <param name="newConfig">The new configuration</param>
    protected virtual void OnConfigurationUpdated(IPragmaticProviderConfiguration oldConfig, IPragmaticProviderConfiguration newConfig)
    {
        // Reinitialize redactor if privacy configuration changed
        if (oldConfig.Privacy.EnableRedaction != newConfig.Privacy.EnableRedaction ||
            oldConfig.Privacy.RedactionMode != newConfig.Privacy.RedactionMode)
        {
            InitializeRedactor();
        }
    }

    /// <summary>
    /// Performs provider-specific health checks. Override to add custom logic.
    /// </summary>
    /// <returns>Health status</returns>
    protected virtual ProviderHealthStatus PerformCustomHealthCheck()
    {
        return ProviderHealthStatus.Healthy;
    }

    /// <summary>
    /// Gets custom metrics specific to this provider. Override to add custom metrics.
    /// </summary>
    /// <returns>Dictionary of custom metrics</returns>
    protected virtual Dictionary<string, object?> GetCustomMetrics()
    {
        return new Dictionary<string, object?>();
    }

    /// <summary>
    /// Called when a write error occurs. Override to handle errors.
    /// </summary>
    /// <param name="logEntry">The log entry that failed</param>
    /// <param name="exception">The exception that occurred</param>
    protected virtual void OnWriteError(LogEntry logEntry, Exception exception)
    {
        // Default implementation does nothing
    }

    /// <summary>
    /// Disposes provider-specific resources. Override to add custom disposal logic.
    /// </summary>
    protected virtual void DisposeCore()
    {
        // Default implementation does nothing
    }

    /// <summary>
    /// Increments the dropped messages counter.
    /// </summary>
    protected void IncrementDroppedMessages()
    {
        Interlocked.Increment(ref _droppedMessages);
    }

    private void ValidateConfiguration()
    {
        var errors = _configuration.Validate();
        if (errors.Any())
        {
            throw new ArgumentException($"Invalid provider configuration: {string.Join(", ", errors)}");
        }
    }

    /// <summary>
    /// Creates a filter context for the current logging operation.
    /// </summary>
    /// <returns>Filter context with available information</returns>
    private LogFilterContext CreateFilterContext()
    {
        var context = new LogFilterContext();

        // Try to get HTTP context if available (ASP.NET Core)
        try
        {
            // This would be injected in a real implementation
            // For now, we'll use a placeholder approach
            var httpContext = GetHttpContextAccessor();
            if (httpContext != null)
            {
                context.HttpContext = httpContext;

                // Extract common properties
                context.RequestPath = GetRequestPath(httpContext);
                context.UserId = GetUserId(httpContext);
                context.CorrelationId = GetCorrelationId(httpContext);
            }
        }
        catch
        {
            // Ignore errors when HTTP context is not available
        }

        return context;
    }

    /// <summary>
    /// Gets HTTP context accessor if available. Override in derived classes for specific implementations.
    /// </summary>
    protected virtual object? GetHttpContextAccessor() => null;

    /// <summary>
    /// Extracts request path from HTTP context. Override for specific implementations.
    /// </summary>
    protected virtual string? GetRequestPath(object httpContext) => null;

    /// <summary>
    /// Extracts user ID from HTTP context. Override for specific implementations.
    /// </summary>
    protected virtual string? GetUserId(object httpContext) => null;

    /// <summary>
    /// Extracts correlation ID from HTTP context. Override for specific implementations.
    /// </summary>
    protected virtual string? GetCorrelationId(object httpContext) => null;

    private void EnrichWithContext(LogEntry logEntry)
    {
        // Add current context properties
        var currentContext = LogContextScope.Current;
        if (currentContext != null)
        {
            foreach (var kvp in currentContext.Properties)
            {
                if (ShouldIncludeContextProperty(kvp.Key))
                {
                    logEntry.Properties[kvp.Key] = kvp.Value;
                }
            }
        }

        // Add context provider properties, layer over layer so the lowest priority value is written last
        // and wins. Static layers only change when providers do, so their filtered copy is kept until the
        // manager's version or this provider's configuration moves; per-call layers (the logging thread,
        // the request) describe this call and are read every time.
        foreach (var layer in FilteredContextLayers())
        {
            if (layer.StaticProperties is { } properties)
            {
                foreach (var kvp in properties)
                    logEntry.Properties[kvp.Key] = kvp.Value;
            }
            else
            {
                WritePerCallProperties(layer.Provider, logEntry.Properties);
            }
        }
    }

    private Func<string, bool>? _includeContextProperty;

    private void WritePerCallProperties(IContextProvider provider, IDictionary<string, object?> target)
    {
        try
        {
            if (!provider.IsAvailable())
                return;

            var include = _includeContextProperty ??= ShouldIncludeContextProperty;
            if (provider is IPerCallContextWriter writer)
            {
                writer.WriteContextProperties(target, include);
                return;
            }

            foreach (var kvp in provider.GetContextProperties())
            {
                if (include(kvp.Key))
                    target[kvp.Key] = kvp.Value;
            }
        }
        catch (Exception ex)
        {
            // As in the manager's aggregation: a failing context provider costs its properties, not the entry.
            RecordError($"Context provider '{provider.Name}' failed: {ex.Message}");
        }
    }

    private FilteredContext? _filteredContext;

    private ContextLayer[] FilteredContextLayers()
    {
        var manager = ContextManager.Instance;
        var version = manager.CacheVersion;
        var configuration = _configuration;

        if (_filteredContext is { } cached && cached.Version == version && ReferenceEquals(cached.Configuration, configuration))
            return cached.Layers;

        var source = manager.Layers();
        var layers = new ContextLayer[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i].StaticProperties is not { } properties)
            {
                layers[i] = source[i];
                continue;
            }

            var filtered = new List<KeyValuePair<string, object?>>(properties.Length);
            foreach (var kvp in properties)
            {
                if (ShouldIncludeContextProperty(kvp.Key))
                    filtered.Add(kvp);
            }

            layers[i] = source[i] with { StaticProperties = filtered.ToArray() };
        }

        _filteredContext = new FilteredContext(version, configuration, layers);
        return layers;
    }

    /// <summary>The context layers with their static properties filtered by this provider, for one manager version.</summary>
    private sealed record FilteredContext(int Version, IPragmaticProviderConfiguration Configuration, ContextLayer[] Layers);

    /// <summary>
    ///     Whether a context property passes the configured filter, decided once per property name.
    /// </summary>
    /// <remarks>
    ///     The same few dozen context properties come through on every call, and the decision depends only
    ///     on the name and the configuration. Computed per call it allocated a closure per property for the
    ///     pattern check, even with no pattern configured. The cache is replaced whenever the configuration
    ///     is (<see cref="UpdateConfiguration" />); a filter mutated in place on the live configuration is
    ///     not seen, as the configuration is not meant to be changed that way.
    /// </remarks>
    private bool ShouldIncludeContextProperty(string propertyName)
    {
        var filter = _configuration.ContextFilter;
        if (filter.Mode == ContextFilterMode.All)
            return true;

        var decisions = _contextDecisions;
        if (decisions.TryGetValue(propertyName, out var include))
            return include;

        include = DecideContextProperty(filter, propertyName);
        decisions.TryAdd(propertyName, include);
        return include;
    }

    private static bool DecideContextProperty(ContextFilterConfiguration filter, string propertyName)
    {
        switch (filter.Mode)
        {
            case ContextFilterMode.Include:
                return filter.PropertyNames.Contains(propertyName) || MatchesAnyPattern(filter, propertyName);

            case ContextFilterMode.Exclude:
                return !filter.PropertyNames.Contains(propertyName) && !MatchesAnyPattern(filter, propertyName);

            case ContextFilterMode.None:
                return false;

            default:
                return true;
        }
    }

    private static bool MatchesAnyPattern(ContextFilterConfiguration filter, string propertyName)
    {
        foreach (var pattern in filter.PropertyPatterns)
        {
            if (IsPatternMatch(propertyName, pattern))
                return true;
        }

        return false;
    }

    private static void FilterStructuredProperties(LogEntry logEntry)
    {
        // Called only when IncludeStructuredProperties is true; apply additional property-level
        // filters here in the future. Currently a no-op: properties are already populated.
    }

    private static bool IsPatternMatch(string input, string pattern)
    {
        // Simple wildcard matching (* and ?)
        if (pattern == "*")
            return true;

        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase);

        // Compile once per distinct pattern, then reuse. Escape the pattern so only the
        // wildcard tokens become regex metacharacters; everything else is matched literally.
        var regex = PatternRegexCache.GetOrAdd(pattern, static p =>
        {
            var regexPattern = "^" + Regex.Escape(p)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });

        return regex.IsMatch(input);
    }

    private void RecordProcessingTime(double milliseconds)
    {
        // Lossy ring buffer: overwrites the oldest sample. Torn reads of a double slot are
        // acceptable for an average-of-samples metric.
        var index = (Interlocked.Increment(ref _processingTimeIndex) - 1) & (ProcessingTimeWindow - 1);
        _processingTimes[index] = milliseconds;
    }

    /// <summary>Counts an entry whose message was written as masked values because it had no template.</summary>
    internal void RecordRedactedWithoutTemplate() => Interlocked.Increment(ref _redactedWithoutTemplate);

    private void RecordError(string error)
    {
        _lastError = error;
        _lastErrorTime = DateTime.UtcNow;
    }

    /// <summary>
    ///     Replaces every structured property whose type declared members with a redacted rendering,
    ///     in place, before any provider sees the entry.
    /// </summary>
    private void ApplyDeclaredRedaction(LogEntry logEntry)
    {
        var redactor = DeclaredRedactor;
        if (redactor is null || redactor.IsEmpty || logEntry.Properties.Count == 0)
            return;

        List<KeyValuePair<string, object?>>? changes = null;
        foreach (var kvp in logEntry.Properties)
        {
            var redacted = redactor.RedactValue(kvp.Value);
            if (ReferenceEquals(redacted, kvp.Value))
                continue;
            (changes ??= []).Add(new KeyValuePair<string, object?>(kvp.Key, redacted));
        }

        if (changes is null)
            return;

        foreach (var change in changes)
            logEntry.Properties[change.Key] = change.Value;
    }

    /// <summary>
    /// Initializes or reinitializes the data redactor based on current privacy configuration.
    /// </summary>
    private void InitializeRedactor()
    {
        if (!_configuration.Privacy.EnableRedaction)
        {
            _dataRedactor = null;
            return;
        }

        var redactorConfig = CreateRedactorConfiguration(_configuration.Privacy);
        _dataRedactor = new PragmaticDataRedactor(redactorConfig);
    }

    /// <summary>
    /// Creates a redactor configuration based on the privacy settings.
    /// </summary>
    /// <param name="privacyConfig">The privacy configuration</param>
    /// <returns>The data redactor configuration</returns>
    private static PragmaticDataRedactorConfiguration CreateRedactorConfiguration(PrivacyConfiguration privacyConfig)
    {
        var redactorConfig = privacyConfig.RedactionMode switch
        {
            RedactionMode.None => new PragmaticDataRedactorConfiguration(),
            RedactionMode.Conservative => PragmaticDataRedactorConfiguration.CreateDefault(),
            RedactionMode.Standard => PragmaticDataRedactorConfiguration.CreateDefault(),
            RedactionMode.Aggressive => PragmaticDataRedactorConfiguration.CreateGdprCompliant(),
            RedactionMode.Custom => new PragmaticDataRedactorConfiguration(),
            _ => PragmaticDataRedactorConfiguration.CreateDefault()
        };

        // Apply custom settings from privacy configuration
        if (privacyConfig.SensitivePropertyNames.Length > 0)
        {
            redactorConfig.SensitivePropertyNames = redactorConfig.SensitivePropertyNames
                .Concat(privacyConfig.SensitivePropertyNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (privacyConfig.PropertyNamePatterns.Length > 0)
        {
            redactorConfig.PropertyNamePatterns = redactorConfig.PropertyNamePatterns
                .Concat(privacyConfig.PropertyNamePatterns)
                .Distinct()
                .ToArray();
        }

        if (privacyConfig.MessageRedactionPatterns.Length > 0)
        {
            redactorConfig.MessageRedactionPatterns = redactorConfig.MessageRedactionPatterns
                .Concat(privacyConfig.MessageRedactionPatterns)
                .Distinct()
                .ToArray();
        }

        // Override redactor settings with privacy config
        redactorConfig.RedactionPlaceholder = privacyConfig.RedactionPlaceholder;
        redactorConfig.PreserveLengths = privacyConfig.PreserveLengths;
        redactorConfig.PreserveJsonStructure = privacyConfig.PreserveJsonStructure;
        redactorConfig.EnableDeepRedaction = privacyConfig.EnableDeepRedaction;

        return redactorConfig;
    }
}