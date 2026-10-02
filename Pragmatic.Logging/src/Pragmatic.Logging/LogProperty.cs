namespace Pragmatic.Logging;

/// <summary>
/// Represents a structured logging property with type safety.
/// </summary>
/// <typeparam name="T">The type of the property value</typeparam>
public readonly struct LogProperty<T> : ILogProperty<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LogProperty{T}"/> struct.
    /// </summary>
    /// <param name="name">The property name</param>
    /// <param name="value">The property value</param>
    /// <param name="destructure">Whether to destructure the value</param>
    public LogProperty(string name, T value, bool destructure = false)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value;
        Destructure = destructure;
        ValueType = typeof(T);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public T Value { get; }

    /// <inheritdoc />
    object? ILogProperty.Value => Value;

    /// <inheritdoc />
    public Type ValueType { get; }

    /// <inheritdoc />
    public bool Destructure { get; }

    /// <summary>
    /// Returns a string representation of this property.
    /// </summary>
    /// <returns>A string in the format "Name = Value"</returns>
    public override string ToString()
    {
        return $"{Name} = {Value}";
    }

    /// <summary>
    /// Determines whether the specified object is equal to this property.
    /// </summary>
    /// <param name="obj">The object to compare</param>
    /// <returns>True if equal, false otherwise</returns>
    public override bool Equals(object? obj)
    {
        return obj is LogProperty<T> other && Equals(other);
    }

    /// <summary>
    /// Determines whether the specified property is equal to this property.
    /// </summary>
    /// <param name="other">The property to compare</param>
    /// <returns>True if equal, false otherwise</returns>
    public bool Equals(LogProperty<T> other)
    {
        return Name == other.Name &&
               EqualityComparer<T>.Default.Equals(Value, other.Value) &&
               Destructure == other.Destructure;
    }

    /// <summary>
    /// Returns the hash code for this property.
    /// </summary>
    /// <returns>The hash code</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Value, Destructure);
    }

    /// <summary>
    /// Determines whether two properties are equal.
    /// </summary>
    /// <param name="left">The left property</param>
    /// <param name="right">The right property</param>
    /// <returns>True if equal, false otherwise</returns>
    public static bool operator ==(LogProperty<T> left, LogProperty<T> right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two properties are not equal.
    /// </summary>
    /// <param name="left">The left property</param>
    /// <param name="right">The right property</param>
    /// <returns>True if not equal, false otherwise</returns>
    public static bool operator !=(LogProperty<T> left, LogProperty<T> right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Provides factory methods for creating log properties.
/// </summary>
public static class LogProperty
{
    /// <summary>
    /// Creates a new log property.
    /// </summary>
    /// <typeparam name="T">The type of the property value</typeparam>
    /// <param name="name">The property name</param>
    /// <param name="value">The property value</param>
    /// <param name="destructure">Whether to destructure the value</param>
    /// <returns>A new log property</returns>
    public static LogProperty<T> Create<T>(string name, T value, bool destructure = false)
    {
        return new LogProperty<T>(name, value, destructure);
    }

    /// <summary>
    /// Creates a new log property that will be destructured.
    /// </summary>
    /// <typeparam name="T">The type of the property value</typeparam>
    /// <param name="name">The property name</param>
    /// <param name="value">The property value</param>
    /// <returns>A new log property marked for destructuring</returns>
    public static LogProperty<T> Destructure<T>(string name, T value)
    {
        return new LogProperty<T>(name, value, destructure: true);
    }

    /// <summary>
    /// Creates a log property from a key-value pair.
    /// </summary>
    /// <typeparam name="T">The type of the property value</typeparam>
    /// <param name="kvp">The key-value pair</param>
    /// <param name="destructure">Whether to destructure the value</param>
    /// <returns>A new log property</returns>
    public static LogProperty<T> FromKeyValuePair<T>(KeyValuePair<string, T> kvp, bool destructure = false)
    {
        return new LogProperty<T>(kvp.Key, kvp.Value, destructure);
    }
}