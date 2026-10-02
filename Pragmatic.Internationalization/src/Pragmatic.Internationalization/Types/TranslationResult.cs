namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Result of a translation lookup via <see cref="Providers.IStringLocalizer" />.
///     Contains the resolved value and indicates whether the translation was found.
/// </summary>
/// <remarks>
///     <para>
///         This type is returned when you explicitly look up a translation key.
///         It differs from <see cref="LocalizationKey" /> (which is just a key) and
///         <see cref="LocalizedString" /> (which stores multiple translations for entity properties).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// var result = localizer["welcome"];
/// if (result.IsMissing)
///     Console.WriteLine($"Missing translation for: {result.Key}");
/// else
///     Console.WriteLine(result.Value);
/// 
/// // Implicit conversion to string
/// string message = localizer["welcome"];
/// </code>
/// </example>
public readonly struct TranslationResult : IEquatable<TranslationResult>
{
    /// <summary>
    ///     Gets the translation key that was looked up.
    /// </summary>
    public string Key { get; }

    /// <summary>
    ///     Gets the resolved translation value.
    ///     If the translation was not found, this equals the key.
    /// </summary>
    public string Value { get; }

    /// <summary>
    ///     Gets whether the translation was successfully found.
    /// </summary>
    public bool IsLocalized { get; }

    /// <summary>
    ///     Gets whether the translation was not found (missing).
    /// </summary>
    public bool IsMissing => !IsLocalized;

    private TranslationResult(string key, string value, bool isLocalized)
    {
        Key = key ?? "";
        Value = value ?? "";
        IsLocalized = isLocalized;
    }

    /// <summary>
    ///     Creates a result for a found translation.
    /// </summary>
    public static TranslationResult Found(string key, string value)
    {
        return new TranslationResult(key, value, true);
    }

    /// <summary>
    ///     Creates a result for a missing translation (key not found).
    /// </summary>
    public static TranslationResult Missing(string key)
    {
        return new TranslationResult(key, key, false);
    }

    /// <summary>
    ///     Implicit conversion to string (returns the value).
    /// </summary>
    public static implicit operator string(TranslationResult result)
    {
        return result.Value;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <inheritdoc />
    public bool Equals(TranslationResult other)
    {
        return Key == other.Key && Value == other.Value && IsLocalized == other.IsLocalized;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is TranslationResult other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Key, Value, IsLocalized);
    }

    /// <summary>
    ///     Equality operator.
    /// </summary>
    public static bool operator ==(TranslationResult left, TranslationResult right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Inequality operator.
    /// </summary>
    public static bool operator !=(TranslationResult left, TranslationResult right)
    {
        return !left.Equals(right);
    }
}