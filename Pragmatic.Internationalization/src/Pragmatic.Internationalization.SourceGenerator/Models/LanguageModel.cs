// =============================================================================
// Pragmatic.Internationalization - LanguageModel
// Immutable model for language code generation caching
// =============================================================================

using System.Collections.Immutable;

namespace Pragmatic.Internationalization.SourceGenerator.Models;

/// <summary>
///     Represents a single ISO 639-1 language for source generation.
/// </summary>
internal readonly record struct LanguageModel(
    string Code,
    string Name,
    string NativeName,
    bool IsRightToLeft,
    string PluralFamily)
{
    /// <summary>
    ///     Gets the escaped name for use in C# string literals.
    /// </summary>
    public string EscapedName => EscapeString(Name);

    /// <summary>
    ///     Gets the escaped native name for use in C# string literals.
    /// </summary>
    public string EscapedNativeName => EscapeString(NativeName);

    /// <summary>
    ///     Gets the property name for this language (PascalCase).
    /// </summary>
    public string PropertyName => ToPascalCase(Name);

    private static string EscapeString(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"");

    private static string ToPascalCase(string name)
    {
        // Handle special cases
        return name switch
        {
            "Norwegian Bokmål" => "NorwegianBokmal",
            "Norwegian Nynorsk" => "NorwegianNynorsk",
            "Scottish Gaelic" => "ScottishGaelic",
            _ => name.Replace(" ", "").Replace("-", "")
        };
    }
}

/// <summary>
///     Aggregated model containing all languages for generation.
/// </summary>
internal readonly record struct LanguageCodeGenerationModel(
    ImmutableArray<LanguageModel> Languages)
{
    /// <summary>
    ///     Gets whether this model has valid languages to generate.
    /// </summary>
    public bool IsValid => !Languages.IsDefaultOrEmpty;
}
