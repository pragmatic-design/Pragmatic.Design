using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Pragmatic.Logging.Context;

/// <summary>
/// Default implementation of ILogContext and IMutableLogContext that provides
/// thread-safe context management with async flow support.
/// </summary>
public sealed class LogContext : IMutableLogContext, IDisposable
{
    private readonly ConcurrentDictionary<string, object?> _properties = new();
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

            if (_parent == null)
                return _properties;

            // Merge parent and current properties, with current taking precedence
            var merged = new Dictionary<string, object?>(_parent.Properties);
            foreach (var kvp in _properties)
            {
                merged[kvp.Key] = kvp.Value;
            }
            return merged;
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? GetProperty(string name)
    {
        ThrowIfDisposed();

        if (_properties.TryGetValue(name, out var value))
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

        return _properties.ContainsKey(name) || (_parent?.HasProperty(name) ?? false);
    }

    /// <inheritdoc />
    public void SetProperty(string name, object? value)
    {
        ThrowIfDisposed();
        _properties[name] = value;
    }

    /// <inheritdoc />
    public bool RemoveProperty(string name)
    {
        ThrowIfDisposed();
        return _properties.TryRemove(name, out _);
    }

    /// <inheritdoc />
    public void Clear()
    {
        ThrowIfDisposed();
        _properties.Clear();
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