// =============================================================================
// Pragmatic.Design - Artifact
// Represents a generated source file artifact
// =============================================================================

using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace Pragmatic.SourceGen;

/// <summary>
///     Represents a generated source file artifact.
/// </summary>
internal readonly struct Artifact
{
    /// <summary>
    ///     The hint name for the generated file (e.g., "MyClass.g.cs").
    /// </summary>
    public string HintName { get; init; }

    private readonly SourceText? _source;

    /// <summary>
    ///     True when the template produced no content — the normal outcome when
    ///     <c>Validate()</c> returns <c>false</c>, which every template uses to mean
    ///     "there is nothing to generate here". Emitting such an artifact would add an
    ///     empty <c>.g.cs</c> to the compilation, so <see cref="SourceOutput.AddSource" />
    ///     skips it.
    /// </summary>
    public bool IsEmpty => _source is null || _source.Length == 0;

    /// <summary>
    ///     The content, when there is any. Returns <c>false</c> for an artifact a template declined to
    ///     produce.
    /// </summary>
    /// <remarks>
    ///     There is deliberately no <c>Source</c> property. <c>ToSourceText()</c> turns a declined
    ///     template into an <b>empty</b> <see cref="SourceText" /> rather than null, so a guard
    ///     comparing that property to null was always true — and 112 of them were written
    ///     across the generator before anyone noticed. Most were harmless, because
    ///     <see cref="SourceOutput.AddSource" /> re-checks; the one that gated a second decision emitted
    ///     a file that should not have existed. The trap was documented here and in
    ///     <c>SourceOutput</c>, and documenting it prevented neither the redundancy nor the bug.
    ///     A caller that cannot name the content without answering "is there any?" cannot get it wrong.
    /// </remarks>
    public bool TryGetSource(out SourceText source)
    {
        source = _source!;
        return !IsEmpty;
    }

    /// <summary>
    ///     The rendered content as text, empty when the template declined to produce anything.
    /// </summary>
    /// <remarks>
    ///     For reading — assertions, diffing, a generator that post-processes text. It is a
    ///     <see cref="string" /> rather than a <see cref="SourceText" /> on purpose: emission goes
    ///     through <see cref="SourceOutput.AddSource" />, and a property that cannot be handed to
    ///     Roslyn cannot become a second emission path.
    /// </remarks>
    public string Text => _source?.ToString() ?? string.Empty;

    /// <summary>
    ///     Creates a new artifact.
    /// </summary>
    /// <param name="hintName">The hint name for the generated file</param>
    /// <param name="source">The source text content</param>
    public Artifact(string hintName, SourceText source)
    {
        HintName = hintName;
        _source = source;
    }

    /// <summary>
    ///     Creates a new artifact from a string.
    /// </summary>
    /// <param name="hintName">The hint name for the generated file</param>
    /// <param name="content">The source code content</param>
    public static Artifact FromString(string hintName, string content)
    {
        // Normalize line endings to CRLF for consistent output on Windows
        content = content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        return new Artifact(hintName, SourceText.From(content, Encoding.UTF8));
    }
}