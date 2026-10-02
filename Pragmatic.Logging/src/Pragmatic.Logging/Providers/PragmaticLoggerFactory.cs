using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Logger factory that combines Microsoft.Extensions.Logging with Pragmatic.Logging providers.
/// This acts as a bridge between the standard ILoggerFactory and our enhanced provider system.
/// </summary>
public sealed class PragmaticLoggerFactory : ILoggerFactory
{
    private readonly ILoggerFactory _innerFactory;
    private readonly PragmaticLoggerProviderRegistry _providerRegistry;
    private readonly GlobalFilterConfiguration _globalFilters;
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new();
    private int _disposed; // 0 = not disposed, 1 = disposed; Interlocked for atomic check-and-set

    /// <summary>
    /// Initializes a new instance of PragmaticLoggerFactory.
    /// </summary>
    /// <param name="innerFactory">The underlying ILoggerFactory to delegate to</param>
    /// <param name="providerRegistry">The Pragmatic.Logging provider registry</param>
    /// <param name="globalFilters">Global filter configuration</param>
    public PragmaticLoggerFactory(ILoggerFactory innerFactory, PragmaticLoggerProviderRegistry providerRegistry, GlobalFilterConfiguration globalFilters)
    {
        _innerFactory = innerFactory ?? throw new ArgumentNullException(nameof(innerFactory));
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        _globalFilters = globalFilters ?? throw new ArgumentNullException(nameof(globalFilters));
    }

    /// <summary>
    /// Gets the provider registry for managing Pragmatic.Logging providers.
    /// </summary>
    public PragmaticLoggerProviderRegistry ProviderRegistry => _providerRegistry;

    /// <summary>
    /// Creates a logger that combines standard and Pragmatic.Logging providers.
    /// </summary>
    /// <param name="categoryName">The category name for the logger</param>
    /// <returns>A composite logger that writes to both standard and Pragmatic providers</returns>
    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        return _loggers.GetOrAdd(categoryName, name =>
        {
            // Get the standard logger from the inner factory
            var standardLogger = _innerFactory.CreateLogger(name);

            // Get Pragmatic.Logging loggers from the registry
            var pragmaticLoggers = _providerRegistry.CreateLoggers(name);

            // Create a composite logger that writes to both
            return new CompositeLogger(name, standardLogger, pragmaticLoggers);
        });
    }

    /// <summary>
    /// Adds a standard ILoggerProvider to the inner factory.
    /// </summary>
    /// <param name="provider">The provider to add</param>
    public void AddProvider(ILoggerProvider provider)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        _innerFactory.AddProvider(provider);
    }

    /// <summary>
    /// Adds a Pragmatic.Logging provider to the registry.
    /// </summary>
    /// <param name="provider">The Pragmatic provider to add</param>
    /// <returns>True if added successfully, false if name conflicts</returns>
    public bool AddPragmaticProvider(IPragmaticLoggerProvider provider)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        return _providerRegistry.RegisterProvider(provider);
    }

    /// <summary>
    /// Removes a Pragmatic.Logging provider from the registry.
    /// </summary>
    /// <param name="providerName">The name of the provider to remove</param>
    /// <returns>True if removed successfully, false if not found</returns>
    public bool RemovePragmaticProvider(string providerName)
    {
        if (_disposed != 0)
            return false;

        var removed = _providerRegistry.UnregisterProvider(providerName);

        // Clear cached loggers to force recreation without this provider
        if (removed)
        {
            _loggers.Clear();
        }

        return removed;
    }

    /// <summary>
    /// Gets metrics for all Pragmatic.Logging providers.
    /// </summary>
    /// <returns>Dictionary of provider metrics</returns>
    public Dictionary<string, ProviderMetrics> GetProviderMetrics()
    {
        if (_disposed != 0)
            return new Dictionary<string, ProviderMetrics>();

        return _providerRegistry.GetAllMetrics();
    }

    /// <summary>
    /// Performs health checks on all Pragmatic.Logging providers.
    /// </summary>
    /// <returns>Dictionary of provider health statuses</returns>
    public Dictionary<string, ProviderHealthStatus> CheckProviderHealth()
    {
        if (_disposed != 0)
            return new Dictionary<string, ProviderHealthStatus>();

        return _providerRegistry.CheckAllHealth();
    }

    /// <summary>
    /// Flushes all providers that support flushing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when all providers have flushed</returns>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed != 0)
            return;

        await _providerRegistry.FlushAllAsync(cancellationToken);
    }

    /// <summary>
    /// Disposes the factory and all its providers.
    /// </summary>
    public void Dispose()
    {
        // Interlocked.Exchange ensures only one thread executes the dispose body
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _loggers.Clear();
        _providerRegistry.Dispose();
        _innerFactory.Dispose();
    }
}

/// <summary>
/// A logger that writes to both standard ILogger providers and Pragmatic.Logging providers.
/// </summary>
internal sealed class CompositeLogger(string categoryName, ILogger standardLogger, ILogger[] pragmaticLoggers)
    : ILogger
{
    private readonly string _categoryName = categoryName;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        var scopes = new List<IDisposable?>();

        try
        {
            var standardScope = standardLogger.BeginScope(state);
            if (standardScope != null)
                scopes.Add(standardScope);
        }
        catch (ObjectDisposedException) { }

        foreach (var logger in pragmaticLoggers)
        {
            try
            {
                var scope = logger.BeginScope(state);
                if (scope != null)
                    scopes.Add(scope);
            }
            catch (ObjectDisposedException) { }
        }

        return scopes.Count == 0 ? null : new CompositeDisposable(scopes);
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        try
        {
            if (standardLogger.IsEnabled(logLevel))
                return true;
        }
        catch (ObjectDisposedException) { }

        foreach (var logger in pragmaticLoggers)
        {
            try
            {
                if (logger.IsEnabled(logLevel))
                    return true;
            }
            catch (ObjectDisposedException) { }
        }

        return false;
    }

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            if (standardLogger.IsEnabled(logLevel))
                standardLogger.Log(logLevel, eventId, state, exception, formatter);
        }
        catch (ObjectDisposedException) { }

        foreach (var logger in pragmaticLoggers)
        {
            try
            {
                // No IsEnabled pre-check: PragmaticLogger.Log performs it as its first step,
                // and double-checking costs a provider-level lookup per call on the hot path.
                logger.Log(logLevel, eventId, state, exception, formatter);
            }
            catch (ObjectDisposedException) { }
        }
    }
}

/// <summary>
/// Disposable that disposes multiple child disposables.
/// </summary>
internal sealed class CompositeDisposable(List<IDisposable?> disposables) : IDisposable
{
    private volatile bool _disposed;

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            foreach (var disposable in disposables)
            {
                try
                {
                    disposable?.Dispose();
                }
                catch (Exception)
                {
                    // Continue disposing other items even if one fails
                }
            }
        }
    }
}