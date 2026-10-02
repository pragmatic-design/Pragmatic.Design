namespace Pragmatic.Logging;

/// <summary>
/// Represents a structured property that can be logged with type safety.
/// </summary>
public interface ILogProperty
{
    /// <summary>
    /// Gets the name of the property.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the value of the property.
    /// </summary>
    object? Value { get; }

    /// <summary>
    /// Gets the type of the property value.
    /// </summary>
    Type ValueType { get; }

    /// <summary>
    /// Indicates whether this property should be destructured when logged.
    /// </summary>
    bool Destructure { get; }
}

/// <summary>
/// Represents a typed structured property that can be logged.
/// </summary>
/// <typeparam name="T">The type of the property value</typeparam>
public interface ILogProperty<out T> : ILogProperty
{
    /// <summary>
    /// Gets the strongly-typed value of the property.
    /// </summary>
    new T Value { get; }
}