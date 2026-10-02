using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.AspNetCore;

/// <summary>
/// Bootstrap logger for early application startup before DI container is fully configured.
/// Provides immediate logging capability with minimal setup and automatic transition to full logging.
/// </summary>
public static class BootstrapLogger
{
    private static readonly Lazy<ILoggerFactory> _bootstrapFactory = new(CreateBootstrapLoggerFactory);
    private static readonly ConcurrentDictionary<string, ILogger> _loggerCache = new();
    private static volatile ILoggerFactory? _transitionedFactory;

    /// <summary>
    /// Gets a logger for the specified category name.
    /// Automatically transitions from bootstrap to full logging when available.
    /// </summary>
    /// <typeparam name="T">The type whose name is used for the logger category name.</typeparam>
    /// <returns>A logger instance.</returns>
    public static ILogger<T> CreateLogger<T>()
    {
        // The underlying factory returns a non-generic ILogger (a CompositeLogger after the
        // transition, a BootstrapLoggerImplementation before it). Casting that to ILogger<T>
        // throws InvalidCastException. Wrap the active factory in the standard Logger<T>, which
        // derives the T-based category and delegates to the factory's CreateLogger(string).
        var factory = _transitionedFactory ?? _bootstrapFactory.Value;
        return new Logger<T>(factory);
    }

    /// <summary>
    /// Gets a logger for the specified category name.
    /// </summary>
    /// <param name="categoryName">The category name for messages produced by the logger.</param>
    /// <returns>A logger instance.</returns>
    public static ILogger CreateLogger(string categoryName)
    {
        // Use transitioned factory if available — single volatile read eliminates TOCTOU
        var transitioned = _transitionedFactory;
        if (transitioned != null)
        {
            return transitioned.CreateLogger(categoryName);
        }

        // Otherwise use cached bootstrap logger
        return _loggerCache.GetOrAdd(categoryName, name => _bootstrapFactory.Value.CreateLogger(name));
    }

    /// <summary>
    /// Transitions from bootstrap logging to the full logging system.
    /// Should be called after the DI container is configured.
    /// </summary>
    /// <param name="loggerFactory">The full logger factory from DI container.</param>
    public static void TransitionToFullLogging(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // Single volatile write — readers that see non-null see a fully initialized factory
        _transitionedFactory = loggerFactory;

        // Log the transition
        var logger = loggerFactory.CreateLogger("Pragmatic.Logging.Bootstrap");
        logger.LogInformation("Transitioned from bootstrap logging to full logging system");

        // Clear bootstrap cache to free memory
        _loggerCache.Clear();
    }

    /// <summary>
    /// Creates a bootstrap logger factory with sensible defaults for early startup.
    /// </summary>
    private static ILoggerFactory CreateBootstrapLoggerFactory()
    {
        var configuration = new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information,
            IncludeStructuredProperties = false, // Keep simple for bootstrap
            IncludeContextEnrichment = false,
            Formatting = new FormattingConfiguration
            {
                TimestampFormat = "HH:mm:ss.fff",
                UseUtcTimestamp = false,
                MessageTemplate = "{Timestamp} [{Level:u3}] {Category}: {Message}",
                IncludeExceptionDetails = true,
                MaxMessageLength = 0
            },
            Performance = new PerformanceConfiguration
            {
                EnableBatching = false, // Immediate output for bootstrap
                UseZeroAllocation = true,
                MaxQueueSize = 100
            }
        };

        var consoleProvider = new PragmaticConsoleProvider("Bootstrap", configuration);

        return new BootstrapLoggerFactory(consoleProvider);
    }

    /// <summary>
    /// Logs application startup information.
    /// </summary>
    /// <param name="applicationName">The name of the application.</param>
    /// <param name="version">The application version.</param>
    /// <param name="environment">The environment name.</param>
    public static void LogApplicationStartup(string applicationName, string? version = null, string? environment = null)
    {
        var logger = CreateLogger("Application.Startup");

        logger.LogInformation("Starting application: {ApplicationName}", applicationName);

        if (!string.IsNullOrEmpty(version))
        {
            logger.LogInformation("Application version: {Version}", version);
        }

        if (!string.IsNullOrEmpty(environment))
        {
            logger.LogInformation("Environment: {Environment}", environment);
        }

        logger.LogInformation("Runtime: {RuntimeVersion} on {OSDescription}",
            Environment.Version, Environment.OSVersion.VersionString);
    }

    /// <summary>
    /// Logs application shutdown information.
    /// </summary>
    /// <param name="applicationName">The name of the application.</param>
    public static void LogApplicationShutdown(string applicationName)
    {
        var logger = CreateLogger("Application.Shutdown");
        logger.LogInformation("Shutting down application: {ApplicationName}", applicationName);
    }
}

/// <summary>
/// Simple logger factory implementation for bootstrap scenarios.
/// </summary>
internal sealed class BootstrapLoggerFactory(PragmaticConsoleProvider consoleProvider) : ILoggerFactory
{
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new();
    private bool _disposed;

    public void AddProvider(ILoggerProvider provider)
    {
        // Bootstrap factory is simple and doesn't support additional providers
        // This is intentional to keep bootstrap lightweight
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(BootstrapLoggerFactory));

        return _loggers.GetOrAdd(categoryName, name => new BootstrapLoggerImplementation(name, consoleProvider));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            consoleProvider.Dispose();
            _loggers.Clear();
            _disposed = true;
        }
    }
}

/// <summary>
/// Simple logger implementation for bootstrap scenarios.
/// </summary>
internal sealed class BootstrapLoggerImplementation(string categoryName, PragmaticConsoleProvider provider) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        // Bootstrap logger has minimal scope support
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return provider.IsEnabled(categoryName, logLevel);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var logEntry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            LogLevel = logLevel,
            EventId = eventId,
            Category = categoryName,
            Message = formatter(state, exception),
            Exception = exception,
            Properties = new Dictionary<string, object?>()
        };

        provider.WriteLog(logEntry);
    }

    /// <summary>
    /// Null scope implementation for bootstrap scenarios.
    /// </summary>
    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}

/// <summary>
/// Extension methods for integrating bootstrap logger with ASP.NET Core.
/// </summary>
public static class BootstrapLoggerExtensions
{
    /// <summary>
    /// Replaces bootstrap logging with the full Pragmatic.Logging system.
    /// Should be called after AddPragmaticLogging().
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication UseBootstrapLoggerTransition(this WebApplication app)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        BootstrapLogger.TransitionToFullLogging(loggerFactory);
        return app;
    }

    /// <summary>
    /// Logs application startup with bootstrap logger.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The web application builder for chaining.</returns>
    public static WebApplicationBuilder LogApplicationStartup(this WebApplicationBuilder builder)
    {
        var assemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName();
        var applicationName = assemblyName?.Name ?? "Unknown";
        var version = assemblyName?.Version?.ToString();
        var environment = builder.Environment.EnvironmentName;

        BootstrapLogger.LogApplicationStartup(applicationName, version, environment);
        return builder;
    }
}