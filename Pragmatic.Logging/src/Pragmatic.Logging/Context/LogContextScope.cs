using System.Runtime.CompilerServices;

namespace Pragmatic.Logging.Context;

/// <summary>
/// Manages the current LogContext using AsyncLocal for proper async/await support.
/// </summary>
/// <remarks>
/// The context lives in the same ambient chain as the logger's scopes (<see cref="AmbientChain" />): a pushed context
/// and a scope begun after it are popped in reverse order, as nested <c>using</c> blocks pop them.
/// </remarks>
public static class LogContextScope
{
    /// <summary>
    /// Gets the current LogContext, or null if none is set.
    /// </summary>
    public static LogContext? Current => AmbientChain.NearestContext(AmbientChain.Current);

    /// <summary>
    /// Pushes a new LogContext to the current scope.
    /// </summary>
    /// <param name="context">The context to push</param>
    /// <returns>A disposable scope that restores the previous context when disposed</returns>
    public static IDisposable Push(LogContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return AmbientChain.PushExisting(context);
    }

    /// <summary>
    /// Creates a new LogContext and pushes it to the current scope.
    /// </summary>
    /// <returns>A disposable scope that restores the previous context when disposed</returns>
    public static IDisposable PushContext()
    {
        var newContext = Current?.CreateChild() ?? new LogContext();
        return AmbientChain.PushNew(newContext);
    }

    /// <summary>
    /// Creates a new LogContext with the specified properties and pushes it to the current scope.
    /// </summary>
    /// <param name="properties">Initial properties for the context</param>
    /// <returns>A disposable scope that restores the previous context when disposed</returns>
    public static IDisposable PushContext(IEnumerable<KeyValuePair<string, object?>> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        // Built at once: setting them one by one copied the properties into a new array per pair.
        var items = LogContextProperties.From(properties);
        var newContext = Current?.CreateChild(items) ?? new LogContext(null, items);
        return AmbientChain.PushNew(newContext);
    }

    /// <summary>
    /// Creates a new LogContext with the specified property and pushes it to the current scope.
    /// </summary>
    /// <param name="name">Property name</param>
    /// <param name="value">Property value</param>
    /// <returns>A disposable scope that restores the previous context when disposed</returns>
    public static IDisposable PushProperty(string name, object? value)
    {
        var newContext = Current?.CreateChild() ?? new LogContext();
        newContext.SetProperty(name, value);
        return AmbientChain.PushNew(newContext);
    }

    /// <summary>
    /// Gets a property from the current context.
    /// </summary>
    /// <param name="name">Property name</param>
    /// <returns>Property value or null if not found</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? GetProperty(string name)
    {
        return Current?.GetProperty(name);
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
        return Current is { } current ? current.GetProperty<T>(name) : default(T);
    }

    /// <summary>
    /// Sets a property in the current context. Creates a new context if none exists.
    /// </summary>
    /// <param name="name">Property name</param>
    /// <param name="value">Property value</param>
    public static void SetProperty(string name, object? value)
    {
        var current = Current;
        if (current == null)
        {
            // Never popped: it lasts until the async flow leaves the method that set it, or until a scope it was
            // pushed over is disposed.
            current = new LogContext();
            AmbientChain.PushNew(current);
        }

        current.SetProperty(name, value);
    }
}
