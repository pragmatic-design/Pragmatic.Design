namespace Pragmatic.SourceGenerator.Features.Serialization.Models;

/// <summary>An enum a writer writes by name, and the converter that may claim it besides <c>JsonStringEnumConverter</c>.</summary>
/// <param name="EnumType">The enum, fully qualified.</param>
/// <param name="FastEnumConverter">
///     Its generated <c>[FastEnum]</c> converter, fully qualified, which writes what <c>JsonStringEnumConverter</c>
///     writes; null for an enum that has none.
/// </param>
internal sealed record JsonEnumConverterModel(string EnumType, string? FastEnumConverter);
