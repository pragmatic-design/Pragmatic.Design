namespace Pragmatic.Logging;

/// <summary>
/// Represents a logging context that provides additional properties to log messages.
/// </summary>
public interface ILogContext
{
    /// <summary>
    /// Gets all properties in this context.
    /// </summary>
    IReadOnlyDictionary<string, object?> Properties { get; }

    /// <summary>
    /// Gets a property value by name.
    /// </summary>
    /// <param name="name">The property name</param>
    /// <returns>The property value, or null if not found</returns>
    object? GetProperty(string name);

    /// <summary>
    /// Gets a strongly-typed property value by name.
    /// </summary>
    /// <typeparam name="T">The expected type of the property</typeparam>
    /// <param name="name">The property name</param>
    /// <returns>The property value, or default(T) if not found or wrong type</returns>
    T? GetProperty<T>(string name);

    /// <summary>
    /// Checks if a property exists in this context.
    /// </summary>
    /// <param name="name">The property name</param>
    /// <returns>True if the property exists, false otherwise</returns>
    bool HasProperty(string name);
}

/// <summary>
/// Represents a mutable logging context that can have properties added.
/// </summary>
public interface IMutableLogContext : ILogContext
{
    /// <summary>
    /// Adds or updates a property in this context.
    /// </summary>
    /// <param name="name">The property name</param>
    /// <param name="value">The property value</param>
    void SetProperty(string name, object? value);

    /// <summary>
    /// Removes a property from this context.
    /// </summary>
    /// <param name="name">The property name</param>
    /// <returns>True if the property was removed, false if it didn't exist</returns>
    bool RemoveProperty(string name);

    /// <summary>
    /// Clears all properties from this context.
    /// </summary>
    void Clear();
}