// =============================================================================
// Pragmatic.Design - CodeWriterOptions
// Configuration for code generation formatting
// =============================================================================

namespace Pragmatic.SourceGen;

/// <summary>
///     Configuration options for code generation formatting.
/// </summary>
internal sealed class CodeWriterOptions
{
    /// <summary>
    ///     The string used for a single indentation level.
    /// </summary>
    public string IndentString { get; init; } = "    ";

    // No brace option: CSharpTemplate always emits braces, so a switch here would change nothing
    // while claiming otherwise.

    /// <summary>
    ///     Default options instance.
    /// </summary>
    public static CodeWriterOptions Default { get; } = new();

    /// <summary>
    ///     Gets the indentation string for the specified level.
    /// </summary>
    /// <param name="indentLevel">The indentation level</param>
    /// <param name="customSpace">Optional custom spacing for alignment</param>
    /// <returns>The indentation string</returns>
    public string String(int indentLevel, int? customSpace = null)
    {
        if (customSpace.HasValue)
            return new string(' ', customSpace.Value);

        return string.Concat(Enumerable.Repeat(IndentString, indentLevel));
    }
}