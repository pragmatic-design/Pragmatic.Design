using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Represents an ISO 639-1 language code with associated metadata.
/// </summary>
/// <remarks>
///     <para>
///         This is an immutable value type representing a language. Language codes are source-generated
///         from ISO 639-1 data, providing IntelliSense support for all standard languages.
///     </para>
///     <para>
///         Use the static properties (e.g., LanguageCode.English, LanguageCode.Italian) for compile-time
///         language references, or <see cref="FromCode" /> for runtime parsing.
///     </para>
/// </remarks>
public readonly partial struct LanguageCode : IEquatable<LanguageCode>
{
    /// <summary>
    ///     Gets the ISO 639-1 two-letter language code (e.g., "en", "it", "de").
    /// </summary>
    public string Code { get; }

    /// <summary>
    ///     Gets the English name of the language (e.g., "English", "Italian", "German").
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the native name of the language (e.g., "English", "Italiano", "Deutsch").
    /// </summary>
    public string NativeName { get; }

    /// <summary>
    ///     Gets a value indicating whether this language is written right-to-left.
    /// </summary>
    /// <remarks>
    ///     True for Arabic (ar), Hebrew (he), Persian (fa), Urdu (ur), and similar languages.
    /// </remarks>
    public bool IsRightToLeft { get; }

    /// <summary>
    ///     Gets the plural rule family for this language.
    /// </summary>
    /// <remarks>
    ///     Used to determine the correct plural form for a given count.
    ///     See <see cref="PluralRuleFamily"/> for available families.
    /// </remarks>
    public PluralRuleFamily PluralFamily { get; }

    /// <summary>
    ///     Creates a new language code instance.
    /// </summary>
    /// <remarks>
    ///     This constructor is internal. Use the static properties or factory methods instead.
    /// </remarks>
    internal LanguageCode(string code, string name, string nativeName, bool isRightToLeft, PluralRuleFamily pluralFamily)
    {
        Code = code;
        Name = name;
        NativeName = nativeName;
        IsRightToLeft = isRightToLeft;
        PluralFamily = pluralFamily;
    }

    /// <summary>
    ///     Creates a LanguageCode from a string code.
    /// </summary>
    /// <param name="code">The ISO 639-1 two-letter language code.</param>
    /// <returns>The corresponding LanguageCode.</returns>
    /// <exception cref="ArgumentException">Thrown if the code is not a valid ISO 639-1 language code.</exception>
    public static LanguageCode FromCode(string code)
    {
        if (!TryFromCode(code, out var language))
            throw new ArgumentException($"'{code}' is not a valid ISO 639-1 language code.", nameof(code));
        return language;
    }

    /// <summary>
    ///     Attempts to create a LanguageCode from a string code.
    /// </summary>
    /// <param name="code">The ISO 639-1 two-letter language code.</param>
    /// <param name="language">When successful, contains the LanguageCode; otherwise, default.</param>
    /// <returns>True if the code is valid; otherwise, false.</returns>
    public static bool TryFromCode(string? code, out LanguageCode language)
    {
        if (string.IsNullOrEmpty(code))
        {
            language = default;
            return false;
        }

        // Generated code will provide the lookup
        return TryFromCodeInternal(code.ToLowerInvariant(), out language);
    }

    /// <summary>
    ///     Checks if a string is a valid ISO 639-1 language code.
    /// </summary>
    /// <param name="code">The code to check.</param>
    /// <returns>True if valid; otherwise, false.</returns>
    public static bool IsValid([NotNullWhen(true)] string? code)
    {
        return TryFromCode(code, out _);
    }

    // This will be implemented by the source generator
    private static partial void TryFromCodeInternal(string code, out LanguageCode language, out bool found);

    private static bool TryFromCodeInternal(string code, out LanguageCode language)
    {
        TryFromCodeInternal(code, out language, out var found);
        return found;
    }

    /// <inheritdoc />
    public bool Equals(LanguageCode other)
    {
        return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is LanguageCode other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(Code ?? string.Empty);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Code ?? string.Empty;
    }

    /// <summary>
    ///     Determines whether two LanguageCode instances are equal.
    /// </summary>
    public static bool operator ==(LanguageCode left, LanguageCode right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two LanguageCode instances are not equal.
    /// </summary>
    public static bool operator !=(LanguageCode left, LanguageCode right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Implicitly converts a LanguageCode to its string representation.
    /// </summary>
    public static implicit operator string(LanguageCode language)
    {
        return language.Code;
    }

    /// <summary>
    ///     Implicitly converts a string to a LanguageCode.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the code is not valid.</exception>
    public static implicit operator LanguageCode(string code)
    {
        return FromCode(code);
    }
}
