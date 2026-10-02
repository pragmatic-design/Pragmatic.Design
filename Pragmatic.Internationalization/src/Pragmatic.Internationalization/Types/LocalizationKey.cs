namespace Pragmatic.Internationalization.Types;

/// <summary>
///     A string that represents a translation key for lookup via <see cref="Providers.ILocalizationProvider" />.
///     The actual translation happens at serialization time, not when the key is created.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="LocalizationKey" /> for error messages, validation messages, and UI strings
///         that need to be translated based on the user's culture.
///     </para>
///     <para>
///         For entity properties that store multiple translations inline (e.g., Product.Name),
///         use <see cref="LocalizedString" /> instead.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Implicit from string
/// LocalizationKey key = "errors.notfound.title";
/// 
/// // In error types
/// public LocalizationKey Title => "errors.notfound.title";
/// 
/// // Convention-based for errors
/// // NotFoundError → "errors.notfound.title"
/// </code>
/// </example>
public readonly struct LocalizationKey : IEquatable<LocalizationKey>
{
    /// <summary>
    ///     Gets the translation key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    ///     Creates a new localization key.
    /// </summary>
    /// <param name="key">The translation key.</param>
    public LocalizationKey(string key)
    {
        Key = key ?? "";
    }

    /// <summary>
    ///     Gets whether this key is empty (no translation needed).
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(Key);

    /// <summary>
    ///     Gets an empty localization key.
    /// </summary>
    public static LocalizationKey Empty => default;

    /// <summary>
    ///     Implicit conversion from string.
    /// </summary>
    public static implicit operator LocalizationKey(string key)
    {
        return new LocalizationKey(key);
    }

    /// <summary>
    ///     Implicit conversion to string.
    /// </summary>
    public static implicit operator string(LocalizationKey key)
    {
        return key.Key;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Key;
    }

    /// <inheritdoc />
    public bool Equals(LocalizationKey other)
    {
        return Key == other.Key;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is LocalizationKey other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Key?.GetHashCode() ?? 0;
    }

    /// <summary>
    ///     Equality operator.
    /// </summary>
    public static bool operator ==(LocalizationKey left, LocalizationKey right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Inequality operator.
    /// </summary>
    public static bool operator !=(LocalizationKey left, LocalizationKey right)
    {
        return !left.Equals(right);
    }
}