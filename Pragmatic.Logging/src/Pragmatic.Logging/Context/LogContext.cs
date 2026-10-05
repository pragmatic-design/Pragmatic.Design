using System.Runtime.CompilerServices;

namespace Pragmatic.Logging.Context;

/// <summary>
/// Default implementation of ILogContext and IMutableLogContext that provides
/// thread-safe context management with async flow support.
/// </summary>
/// <remarks>
/// <para>
/// Properties are copy-on-write: a write publishes a new dictionary, and a read sees an immutable snapshot
/// that is safe to enumerate while another flow writes. A context is typically created per request and
/// written a handful of times, then read on every log call. It used to be a <c>ConcurrentDictionary</c>,
/// whose construction alone allocates a lock object per processor, paid on every context created.
/// </para>
/// <para>
/// <see cref="Properties" /> returns that snapshot: a reference held across a later write does not see it.
/// </para>
/// </remarks>
public sealed class LogContext : IMutableLogContext, IDisposable
{
    private static readonly IReadOnlyDictionary<string, object?> NoProperties = new Dictionary<string, object?>();

    private readonly Lock _writeLock = new();
    private volatile Dictionary<string, object?>? _properties;
    private readonly LogContext? _parent;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the LogContext class.
    /// </summary>
    public LogContext()
    {
    }

    /// <summary>
    /// Initializes a new instance of the LogContext class with a parent context.
    /// </summary>
    /// <param name="parent">The parent context to inherit properties from</param>
    public LogContext(LogContext? parent)
    {
        _parent = parent;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Properties
    {
        get
        {
            ThrowIfDisposed();

            var own = _properties;
            if (_parent == null)
                return own ?? NoProperties;

            // Merge parent and current properties, with current taking precedence
            var merged = new Dictionary<string, object?>(_parent.Properties);
            if (own != null)
            {
                foreach (var kvp in own)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
            return merged;
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? GetProperty(string name)
    {
        ThrowIfDisposed();

        if (_properties is { } own && own.TryGetValue(name, out var value))
            return value;

        return _parent?.GetProperty(name);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetProperty<T>(string name)
    {
        var value = GetProperty(name);
        return value is T typedValue ? typedValue : default(T);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasProperty(string name)
    {
        ThrowIfDisposed();

        return (_properties?.ContainsKey(name) ?? false) || (_parent?.HasProperty(name) ?? false);
    }

    /// <inheritdoc />
    public void SetProperty(string name, object? value)
    {
        ThrowIfDisposed();

        lock (_writeLock)
        {
            var copy = _properties is { } own
                ? new Dictionary<string, object?>(own)
                : new Dictionary<string, object?>(capacity: 4);
            copy[name] = value;
            _properties = copy;
        }
    }

    /// <inheritdoc />
    public bool RemoveProperty(string name)
    {
        ThrowIfDisposed();

        lock (_writeLock)
        {
            if (_properties is not { } own || !own.ContainsKey(name))
                return false;

            var copy = new Dictionary<string, object?>(own);
            copy.Remove(name);
            _properties = copy;
            return true;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        ThrowIfDisposed();
        _properties = null;
    }

    /// <summary>
    /// Creates a child context that inherits from this context.
    /// </summary>
    /// <returns>A new LogContext with this context as parent</returns>
    public LogContext CreateChild()
    {
        ThrowIfDisposed();
        return new LogContext(this);
    }

    /// <summary>
    /// Gets the parent context, if any.
    /// </summary>
    public LogContext? Parent => _parent;

    /// <summary>
    /// Gets the root context in the hierarchy.
    /// </summary>
    public LogContext Root
    {
        get
        {
            var current = this;
            while (current._parent != null)
                current = current._parent;
            return current;
        }
    }

    /// <summary>
    /// Creates a scope that applies this context for the duration of the scope.
    /// </summary>
    /// <returns>A disposable scope that restores the previous context when disposed</returns>
    public IDisposable BeginScope()
    {
        return LogContextScope.Push(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            Clear();
            _disposed = true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}