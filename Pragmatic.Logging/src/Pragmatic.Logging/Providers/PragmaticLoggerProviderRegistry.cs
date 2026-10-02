using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Registry for managing multiple Pragmatic.Logging providers with lifecycle management.
/// </summary>
public sealed class PragmaticLoggerProviderRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, IPragmaticLoggerProvider> _providers = new();
    private int _disposed; // 0 = not disposed; Interlocked for atomic check-and-set

    /// <summary>
    /// Gets all registered providers.
    /// </summary>
    public IReadOnlyDictionary<string, IPragmaticLoggerProvider> Providers => _providers;

    /// <summary>
    /// Registers a new provider with the registry.
    /// </summary>
    /// <param name="provider">The provider to register</param>
    /// <returns>True if registered successfully, false if name conflicts</returns>
    public bool RegisterProvider(IPragmaticLoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        return _providers.TryAdd(provider.Name, provider);
    }

    /// <summary>
    /// Unregisters a provider from the registry.
    /// </summary>
    /// <param name="providerName">The name of the provider to unregister</param>
    /// <returns>True if unregistered successfully, false if not found</returns>
    public bool UnregisterProvider(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (_providers.TryRemove(providerName, out var provider))
        {
            provider.Dispose();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a provider by name.
    /// </summary>
    /// <param name="providerName">The provider name</param>
    /// <returns>The provider if found, null otherwise</returns>
    public IPragmaticLoggerProvider? GetProvider(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (_disposed != 0)
            return null;

        _providers.TryGetValue(providerName, out var provider);
        return provider;
    }

    /// <summary>
    /// Updates configuration for a specific provider.
    /// </summary>
    /// <param name="providerName">The provider name</param>
    /// <param name="configuration">The new configuration</param>
    /// <returns>True if updated successfully, false if provider not found</returns>
    public bool UpdateProviderConfiguration(string providerName, IPragmaticProviderConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(configuration);

        if (_disposed != 0)
            return false;

        var provider = GetProvider(providerName);
        if (provider != null)
        {
            provider.UpdateConfiguration(configuration);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets metrics for all registered providers.
    /// </summary>
    /// <returns>Dictionary of provider metrics</returns>
    public Dictionary<string, ProviderMetrics> GetAllMetrics()
    {
        if (_disposed != 0)
            return new Dictionary<string, ProviderMetrics>();

        var metrics = new Dictionary<string, ProviderMetrics>();

        foreach (var kvp in _providers)
        {
            try
            {
                metrics[kvp.Key] = kvp.Value.GetMetrics();
            }
            catch (Exception)
            {
                // Provider may be disposed or in error state
                metrics[kvp.Key] = new ProviderMetrics
                {
                    LastError = "Failed to retrieve metrics",
                    LastErrorTime = DateTime.UtcNow
                };
            }
        }

        return metrics;
    }

    /// <summary>
    /// Performs health checks on all registered providers.
    /// </summary>
    /// <returns>Dictionary of provider health statuses</returns>
    public Dictionary<string, ProviderHealthStatus> CheckAllHealth()
    {
        if (_disposed != 0)
            return new Dictionary<string, ProviderHealthStatus>();

        var healthStatuses = new Dictionary<string, ProviderHealthStatus>();

        foreach (var kvp in _providers)
        {
            try
            {
                healthStatuses[kvp.Key] = kvp.Value.CheckHealth();
            }
            catch (Exception)
            {
                // Provider may be disposed or in error state
                healthStatuses[kvp.Key] = ProviderHealthStatus.Unhealthy;
            }
        }

        return healthStatuses;
    }

    /// <summary>
    /// Gets the overall health status across all providers.
    /// </summary>
    /// <returns>The worst health status among all providers</returns>
    public ProviderHealthStatus GetOverallHealth()
    {
        var healthStatuses = CheckAllHealth();

        if (healthStatuses.Count == 0)
            return ProviderHealthStatus.Healthy;

        // Return the worst status
        if (healthStatuses.ContainsValue(ProviderHealthStatus.Unhealthy))
            return ProviderHealthStatus.Unhealthy;
        if (healthStatuses.ContainsValue(ProviderHealthStatus.Degraded))
            return ProviderHealthStatus.Degraded;
        if (healthStatuses.ContainsValue(ProviderHealthStatus.Warning))
            return ProviderHealthStatus.Warning;

        return ProviderHealthStatus.Healthy;
    }

    /// <summary>
    /// Creates loggers from all enabled providers for the specified category.
    /// </summary>
    /// <param name="categoryName">The logger category name</param>
    /// <returns>Array of loggers from all providers that support the category</returns>
    public ILogger[] CreateLoggers(string categoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        if (_disposed != 0)
            return Array.Empty<ILogger>();

        var loggers = new List<ILogger>();

        foreach (var provider in _providers.Values)
        {
            try
            {
                var logger = provider.CreateLogger(categoryName);
                if (logger != null)
                {
                    loggers.Add(logger);
                }
            }
            catch (Exception)
            {
                // Continue with other providers even if one fails
            }
        }

        return loggers.ToArray();
    }

    /// <summary>
    /// Flushes all providers that support flushing.
    /// </summary>
    /// <returns>Task that completes when all providers have flushed</returns>
    public async Task FlushAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed != 0)
            return;

        var flushTasks = new List<Task>();

        foreach (var provider in _providers.Values)
        {
            if (provider is IAsyncFlushable flushableProvider)
            {
                flushTasks.Add(flushableProvider.FlushAsync(cancellationToken));
            }
            else if (provider is ISyncFlushable syncFlushableProvider)
            {
                flushTasks.Add(Task.Run(syncFlushableProvider.Flush, cancellationToken));
            }
        }

        if (flushTasks.Count > 0)
        {
            await Task.WhenAll(flushTasks);
        }
    }

    /// <summary>
    /// Disposes all registered providers and clears the registry.
    /// </summary>
    public void Dispose()
    {
        // Interlocked.Exchange ensures only one thread executes the dispose body
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Dispose all providers — ConcurrentDictionary.Values enumeration is safe under concurrent access
        foreach (var provider in _providers.Values)
        {
            try
            {
                provider.Dispose();
            }
            catch (Exception)
            {
                // Continue disposing other providers even if one fails
            }
        }

        _providers.Clear();
    }
}

/// <summary>
/// Interface for providers that support asynchronous flushing.
/// </summary>
public interface IAsyncFlushable
{
    /// <summary>
    /// Flushes pending log entries asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when flushing is done</returns>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for providers that support synchronous flushing.
/// </summary>
public interface ISyncFlushable
{
    /// <summary>
    /// Flushes pending log entries synchronously.
    /// </summary>
    void Flush();
}