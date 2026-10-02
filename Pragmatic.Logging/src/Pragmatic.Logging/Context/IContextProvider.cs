namespace Pragmatic.Logging.Context;

/// <summary>
/// Provides context properties from external sources.
/// </summary>
public interface IContextProvider
{
    /// <summary>
    /// Gets the provider name for identification.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the priority order (lower values = higher priority).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Provides context properties that should be added to log messages.
    /// </summary>
    /// <returns>Dictionary of context properties</returns>
    IReadOnlyDictionary<string, object?> GetContextProperties();

    /// <summary>
    /// Determines if this provider is available in the current environment.
    /// </summary>
    /// <returns>True if provider can provide context, false otherwise</returns>
    bool IsAvailable();
}

/// <summary>
/// Base class for context providers with common functionality.
/// </summary>
public abstract class ContextProviderBase(string name, int priority = 100) : IContextProvider
{
    /// <inheritdoc />
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <inheritdoc />
    public int Priority { get; } = priority;

    /// <inheritdoc />
    public abstract IReadOnlyDictionary<string, object?> GetContextProperties();

    /// <inheritdoc />
    public virtual bool IsAvailable() => true;

    /// <summary>
    /// Helper method to create a read-only dictionary from properties.
    /// </summary>
    /// <param name="properties">Properties to include</param>
    /// <returns>Read-only dictionary</returns>
    protected static IReadOnlyDictionary<string, object?> CreatePropertiesDictionary(
        params (string name, object? value)[] properties)
    {
        var dict = new Dictionary<string, object?>(properties.Length);
        foreach (var (name, value) in properties)
        {
            if (value != null)
                dict[name] = value;
        }
        return dict;
    }
}