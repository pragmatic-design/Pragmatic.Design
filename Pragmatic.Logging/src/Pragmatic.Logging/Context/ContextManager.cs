using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Pragmatic.Logging.Context;

/// <summary>
/// Enhanced context manager with dependency injection support and improved testability.
/// Replaces the original singleton-only ContextManager while maintaining backward compatibility.
/// </summary>
/// <remarks>
/// <para>
/// This implementation provides both singleton access for backward compatibility and full
/// dependency injection support for modern applications. It includes enhanced features
/// like performance metrics, events, and improved async support.
/// </para>
/// </remarks>
public sealed class ContextManager : IContextManager, IDisposable
{
    private readonly List<IContextProvider> _providers = new();
    private readonly ConcurrentDictionary<string, object?> _cache = new();
    private readonly ConcurrentDictionary<string, Task<object?>> _asyncCache = new();
    private readonly object _providersLock = new();
    private volatile bool _cacheInvalid = true;
    private volatile bool _disposed;

    // Performance monitoring
    private long _contextRequests;
    private long _cacheHits;
    private long _cacheMisses;

    /// <summary>
    /// Gets the singleton instance for backward compatibility.
    /// </summary>
    public static ContextManager Instance { get; } = new(registerDefaultProviders: true);

    /// <inheritdoc />
    public int ProviderCount
    {
        get
        {
            lock (_providersLock)
            {
                return _providers.Count;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<ContextProvidersChangedEventArgs>? ProvidersChanged;

    /// <summary>
    /// Initializes a new instance of the ContextManager.
    /// </summary>
    /// <param name="registerDefaultProviders">Whether to register default system providers</param>
    public ContextManager(bool registerDefaultProviders = true)
    {
        if (registerDefaultProviders)
        {
            RegisterDefaultProviders();
        }
    }

    /// <inheritdoc />
    public void RegisterProvider(IContextProvider provider)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(provider);

        IContextProvider? replacedProvider = null;

        lock (_providersLock)
        {
            // Remove existing provider with same name
            var existingIndex = _providers.FindIndex(p => p.Name == provider.Name);
            if (existingIndex >= 0)
            {
                replacedProvider = _providers[existingIndex];
                _providers.RemoveAt(existingIndex);
            }

            // Add new provider and sort by priority
            _providers.Add(provider);
            _providers.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            InvalidateCacheInternal();
        }

        // Raise event outside lock
        ProvidersChanged?.Invoke(this, new ContextProvidersChangedEventArgs(
            ContextProviderChangeType.Added, provider.Name, provider));
    }

    /// <inheritdoc />
    public bool UnregisterProvider(string providerName)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(providerName);

        IContextProvider? removedProvider = null;

        lock (_providersLock)
        {
            var existingIndex = _providers.FindIndex(p => p.Name == providerName);
            if (existingIndex >= 0)
            {
                removedProvider = _providers[existingIndex];
                _providers.RemoveAt(existingIndex);
                InvalidateCacheInternal();
            }
        }

        if (removedProvider != null)
        {
            ProvidersChanged?.Invoke(this, new ContextProvidersChangedEventArgs(
                ContextProviderChangeType.Removed, providerName));
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public IContextProvider[] GetProviders()
    {
        ThrowIfDisposed();

        lock (_providersLock)
        {
            return _providers.ToArray();
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyDictionary<string, object?> GetContextProperties()
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _contextRequests);

        // Single read of volatile flag to avoid TOCTOU between flag and cache count
        if (!_cacheInvalid)
        {
            Interlocked.Increment(ref _cacheHits);
            return _cache;
        }

        Interlocked.Increment(ref _cacheMisses);
        return GetContextPropertiesInternal();
    }

    private IReadOnlyDictionary<string, object?> GetContextPropertiesInternal()
    {
        var aggregatedProperties = new Dictionary<string, object?>();

        IContextProvider[] providers;
        lock (_providersLock)
        {
            providers = _providers.ToArray();
        }

        foreach (var provider in providers)
        {
            try
            {
                if (!provider.IsAvailable())
                    continue;

                var contextProperties = provider.GetContextProperties();
                if (contextProperties.Count > 0)
                {
                    foreach (var kvp in contextProperties)
                    {
                        aggregatedProperties[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception)
            {
                continue;
            }
        }

        _cache.Clear();
        foreach (var kvp in aggregatedProperties)
        {
            _cache.TryAdd(kvp.Key, kvp.Value);
        }
        _cacheInvalid = false;

        return _cache;
    }

    /// <inheritdoc />
    public void InvalidateCache()
    {
        ThrowIfDisposed();
        InvalidateCacheInternal();
    }

    private void InvalidateCacheInternal()
    {
        _cacheInvalid = true;
        _cache.Clear();
        _asyncCache.Clear();
    }

    /// <inheritdoc />
    public void RegisterProviders(IEnumerable<IContextProvider> providers)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(providers);

        var providerList = providers.ToList();
        if (providerList.Count == 0)
            return;

        lock (_providersLock)
        {
            foreach (var provider in providerList)
            {
                ArgumentNullException.ThrowIfNull(provider);
                _providers.RemoveAll(p => p.Name == provider.Name);
                _providers.Add(provider);
            }

            _providers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            InvalidateCacheInternal();
        }

        ProvidersChanged?.Invoke(this, new ContextProvidersChangedEventArgs(
            ContextProviderChangeType.BulkAdded, $"{providerList.Count} providers", null));
    }

    /// <inheritdoc />
    public bool HasProvider(string providerName)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(providerName);

        lock (_providersLock)
        {
            return _providers.Any(p => p.Name == providerName);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, object?>> GetAggregateContextAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _contextRequests);

        if (!_cacheInvalid)
        {
            Interlocked.Increment(ref _cacheHits);
            return _cache;
        }

        Interlocked.Increment(ref _cacheMisses);
        return await GetAggregateContextInternalAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<object?> GetContextPropertyAsync(string providerName, string propertyName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(providerName);
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        IContextProvider? targetProvider;
        lock (_providersLock)
        {
            targetProvider = _providers.FirstOrDefault(p => p.Name == providerName);
        }

        if (targetProvider == null)
            return Task.FromResult<object?>(null);

        try
        {
            var contextProperties = targetProvider.GetContextProperties();
            contextProperties.TryGetValue(propertyName, out var value);
            return Task.FromResult(value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Task.FromResult<object?>(null);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, object?>> GetProviderContextAsync(string providerName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(providerName);

        IContextProvider? targetProvider;
        lock (_providersLock)
        {
            targetProvider = _providers.FirstOrDefault(p => p.Name == providerName);
        }

        if (targetProvider == null)
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());

        try
        {
            var contextProperties = targetProvider.GetContextProperties();
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>(contextProperties));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
        }
    }

    private Task<IReadOnlyDictionary<string, object?>> GetAggregateContextInternalAsync(CancellationToken cancellationToken)
    {
        var aggregatedProperties = new Dictionary<string, object?>();

        IContextProvider[] providers;
        lock (_providersLock)
        {
            providers = _providers.ToArray();
        }

        foreach (var provider in providers)
        {
            try
            {
                if (!provider.IsAvailable())
                    continue;

                var contextProperties = provider.GetContextProperties();
                if (contextProperties.Count > 0)
                {
                    foreach (var kvp in contextProperties)
                    {
                        aggregatedProperties[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception)
            {
                continue;
            }
        }

        _cache.Clear();
        foreach (var kvp in aggregatedProperties)
        {
            _cache.TryAdd(kvp.Key, kvp.Value);
        }
        _cacheInvalid = false;

        return Task.FromResult<IReadOnlyDictionary<string, object?>>(_cache);
    }

    private void RegisterDefaultProviders()
    {
        RegisterProvider(new Providers.MachineContextProvider());
        RegisterProvider(new Providers.ProcessContextProvider());
        RegisterProvider(new Providers.ThreadContextProvider());
    }

    /// <summary>
    /// Gets performance metrics for the context manager.
    /// </summary>
    public ContextManagerMetrics GetMetrics()
    {
        ThrowIfDisposed();

        var requests = Interlocked.Read(ref _contextRequests);
        var hits = Interlocked.Read(ref _cacheHits);
        var misses = Interlocked.Read(ref _cacheMisses);

        return new ContextManagerMetrics
        {
            TotalRequests = requests,
            CacheHits = hits,
            CacheMisses = misses,
            CacheHitRatio = requests > 0 ? (double)hits / requests : 0.0,
            RegisteredProviders = ProviderCount,
            CacheSize = _cache.Count
        };
    }

    /// <summary>
    /// Creates a LogContext populated with current context properties.
    /// </summary>
    /// <returns>New LogContext with current context properties</returns>
    public LogContext CreateContextWithProperties()
    {
        var context = new LogContext();
        var properties = GetContextProperties();

        foreach (var kvp in properties)
        {
            context.SetProperty(kvp.Key, kvp.Value);
        }

        return context;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _cache.Clear();
        _asyncCache.Clear();

        lock (_providersLock)
        {
            foreach (var provider in _providers.OfType<IDisposable>())
            {
                try
                {
                    provider.Dispose();
                }
                catch
                {
                    // Ignore disposal exceptions
                }
            }
            _providers.Clear();
        }

        ProvidersChanged = null;
    }
}

/// <summary>
/// Performance metrics for the context manager.
/// </summary>
public sealed record ContextManagerMetrics
{
    /// <summary>Gets the total number of context requests.</summary>
    public long TotalRequests { get; init; }

    /// <summary>Gets the number of cache hits.</summary>
    public long CacheHits { get; init; }

    /// <summary>Gets the number of cache misses.</summary>
    public long CacheMisses { get; init; }

    /// <summary>Gets the cache hit ratio (0.0 to 1.0).</summary>
    public double CacheHitRatio { get; init; }

    /// <summary>Gets the number of registered providers.</summary>
    public int RegisteredProviders { get; init; }

    /// <summary>Gets the current cache size.</summary>
    public int CacheSize { get; init; }
}
