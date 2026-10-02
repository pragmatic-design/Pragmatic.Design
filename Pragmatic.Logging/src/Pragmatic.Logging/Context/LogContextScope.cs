using System.Runtime.CompilerServices;

namespace Pragmatic.Logging.Context;

/// <summary>
/// Manages the current LogContext using AsyncLocal for proper async/await support.
/// </summary>
public static class LogContextScope
{
    private static readonly AsyncLocal<LogContext?> _current = new();

    /// <summary>
    /// Gets the current LogContext, or null if none is set.
    /// </summary>
    public static LogContext? Current => _current.Value;

    /// <summary>
    /// Pushes a new LogContext to the current scope.
    /// </summary>
    /// <param name="context">The context to push</param>
    /// <returns>A disposable scope that restores the previous context when disposed</returns>
    public static IDisposable Push(LogContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Scope(context);
    }

    /// <summary>
    /// Creates a new LogContext and pushes it to the current scope.
    /// </summary>
    /// <returns>A disposable scope that restores the previous context when disposed</returns>
    public static IDisposable PushContext()
    {
        var newContext = _current.Value?.CreateChild() ?? new LogContext();
        return new Scope(newContext);
    }

    /// <summary>
    /// Creates a new LogContext with the specified properties and pushes it to the current scope.
    /// </summary>
    /// <param name="properties">Initial properties for the context</param>
    /// <returns>A disposable scope that restores the previous context when disposed</returns>
    public static IDisposable PushContext(IEnumerable<KeyValuePair<string, object?>> properties)
    {
        var newContext = _current.Value?.CreateChild() ?? new LogContext();

        foreach (var kvp in properties)
        {
            newContext.SetProperty(kvp.Key, kvp.Value);
        }

        return new Scope(newContext);
    }

    /// <summary>
    /// Creates a new LogContext with the specified property and pushes it to the current scope.
    /// </summary>
    /// <param name="name">Property name</param>
    /// <param name="value">Property value</param>
    /// <returns>A disposable scope that restores the previous context when disposed</returns>
    public static IDisposable PushProperty(string name, object? value)
    {
        var newContext = _current.Value?.CreateChild() ?? new LogContext();
        newContext.SetProperty(name, value);
        return new Scope(newContext);
    }

    /// <summary>
    /// Gets a property from the current context.
    /// </summary>
    /// <param name="name">Property name</param>
    /// <returns>Property value or null if not found</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? GetProperty(string name)
    {
        return _current.Value?.GetProperty(name);
    }

    /// <summary>
    /// Gets a strongly-typed property from the current context.
    /// </summary>
    /// <typeparam name="T">Expected property type</typeparam>
    /// <param name="name">Property name</param>
    /// <returns>Property value or default(T) if not found or wrong type</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? GetProperty<T>(string name)
    {
        return _current.Value != null ? _current.Value.GetProperty<T>(name) : default(T);
    }

    /// <summary>
    /// Sets a property in the current context. Creates a new context if none exists.
    /// </summary>
    /// <param name="name">Property name</param>
    /// <param name="value">Property value</param>
    public static void SetProperty(string name, object? value)
    {
        if (_current.Value == null)
        {
            _current.Value = new LogContext();
        }

        _current.Value.SetProperty(name, value);
    }

    private sealed class Scope : IDisposable
    {
        private readonly LogContext? _previousContext;
        private bool _disposed;

        public Scope(LogContext context)
        {
            _previousContext = _current.Value;
            _current.Value = context;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _current.Value = _previousContext;
                _disposed = true;
            }
        }
    }
}