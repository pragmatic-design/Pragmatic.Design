using System.Runtime.CompilerServices;

namespace Pragmatic.Logging.Context;

/// <summary>
/// Default implementation of ILogContext and IMutableLogContext that provides
/// thread-safe context management with async flow support.
/// </summary>
/// <remarks>
/// <para>
/// Properties are copy-on-write: a write publishes a new immutable array by compare-and-swap, and a read sees
/// one that is safe to walk while another flow writes. A context is typically created per request and written
/// a handful of times, then read on every log call. It used to be a <c>ConcurrentDictionary</c>, whose
/// construction alone allocates a lock object per processor, and then a dictionary copied under a lock on
/// every write.
/// </para>
/// <para>
/// <see cref="Properties" /> returns a snapshot (<see cref="LogContextProperties" />): a reference held across a
/// later write does not see it.
/// </para>
/// </remarks>
public sealed class LogContext : IMutableLogContext, IDisposable
{
    private KeyValuePair<string, object?>[] _items = [];

    // The dictionary view of _items last handed out, kept while _items is the array it reads.
    private LogContextProperties? _view;

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

            var own = Volatile.Read(ref _items);
            if (_parent == null)
            {
                if (Volatile.Read(ref _view) is { } view && ReferenceEquals(view.Items, own))
                    return view;

                var fresh = new LogContextProperties(own);
                Volatile.Write(ref _view, fresh);
                return fresh;
            }

            // Merge parent and current properties, with current taking precedence
            var merged = new Dictionary<string, object?>(_parent.Properties);
            foreach (var property in own)
                merged[property.Key] = property.Value;
            return merged;
        }
    }

    /// <summary>
    ///     Writes the properties <see cref="Properties" /> holds into <paramref name="target" />, those
    ///     <paramref name="include" /> accepts, without building the merged dictionary: the parent's first, this
    ///     context's over them.
    /// </summary>
    internal void WriteProperties(IDictionary<string, object?> target, Func<string, bool> include)
    {
        ThrowIfDisposed();

        _parent?.WriteProperties(target, include);

        foreach (var property in Volatile.Read(ref _items))
        {
            if (include(property.Key))
                target[property.Key] = property.Value;
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? GetProperty(string name)
    {
        ThrowIfDisposed();

        var own = Volatile.Read(ref _items);
        if (LogContextProperties.IndexOf(own, name) is var index and >= 0)
            return own[index].Value;

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

        return LogContextProperties.IndexOf(Volatile.Read(ref _items), name) >= 0 || (_parent?.HasProperty(name) ?? false);
    }

    /// <inheritdoc />
    public void SetProperty(string name, object? value)
    {
        ThrowIfDisposed();

        // Two flows writing at once: the one whose array was replaced under it builds again on the new one.
        var current = Volatile.Read(ref _items);
        while (true)
        {
            var observed = Interlocked.CompareExchange(ref _items, LogContextProperties.With(current, name, value), current);
            if (ReferenceEquals(observed, current))
                return;
            current = observed;
        }
    }

    /// <inheritdoc />
    public bool RemoveProperty(string name)
    {
        ThrowIfDisposed();

        var current = Volatile.Read(ref _items);
        while (true)
        {
            var next = LogContextProperties.Without(current, name);
            if (ReferenceEquals(next, current))
                return false;

            var observed = Interlocked.CompareExchange(ref _items, next, current);
            if (ReferenceEquals(observed, current))
                return true;
            current = observed;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        ThrowIfDisposed();
        Volatile.Write(ref _items, []);
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
