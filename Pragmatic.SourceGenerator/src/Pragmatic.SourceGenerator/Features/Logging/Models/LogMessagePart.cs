namespace Pragmatic.SourceGenerator.Features.Logging.Models;

/// <summary>One part of the rendered message: literal text, or the value of a parameter.</summary>
/// <param name="Literal">The text, already unescaped; null for a value.</param>
/// <param name="ParameterIndex">The parameter whose value goes here; -1 for literal text.</param>
/// <param name="Format">The placeholder's format string, or empty.</param>
internal sealed record LogMessagePart(string? Literal, int ParameterIndex, string Format);
