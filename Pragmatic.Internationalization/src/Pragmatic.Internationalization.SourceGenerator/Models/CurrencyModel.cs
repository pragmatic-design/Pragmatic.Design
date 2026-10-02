// =============================================================================
// Pragmatic.Internationalization - CurrencyModel
// Immutable model for currency code generation caching
// =============================================================================

using System.Collections.Immutable;

namespace Pragmatic.Internationalization.SourceGenerator.Models;

/// <summary>
///     Represents a single ISO 4217 currency for source generation.
/// </summary>
internal readonly record struct CurrencyModel(
    string Code,
    string Name,
    string Symbol,
    int MinorUnits)
{
    /// <summary>
    ///     Gets the escaped name for use in C# string literals.
    /// </summary>
    public string EscapedName => Name
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"");

    /// <summary>
    ///     Gets the escaped symbol for use in C# string literals.
    /// </summary>
    public string EscapedSymbol => Symbol
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"");
}

/// <summary>
///     Aggregated model containing all currencies for generation.
/// </summary>
internal readonly record struct CurrencyCodeGenerationModel(
    ImmutableArray<CurrencyModel> Currencies)
{
    /// <summary>
    ///     Gets whether this model has valid currencies to generate.
    /// </summary>
    public bool IsValid => !Currencies.IsDefaultOrEmpty;
}