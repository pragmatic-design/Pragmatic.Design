// =============================================================================
// Pragmatic.Design - SourceOutput
// Single emission point for generated artifacts
// =============================================================================

using Microsoft.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     The one way a feature hands a generated <see cref="Artifact" /> to Roslyn.
/// </summary>
/// <remarks>
///     A template whose <c>Validate()</c> returns <c>false</c> renders to empty content
///     rather than to nothing — that is how every template says "nothing to generate here".
///     Emitting such an artifact puts an empty <c>.g.cs</c> into the compilation: harmless to
///     compile, but it pollutes the generated output and hides the difference between
///     "deliberately nothing" and "the template silently produced nothing". Routing every
///     emission through here keeps that decision in one place instead of leaving it to each of
///     the ~200 call sites.
///     <para>
///         Call sites do not guard. <see cref="Artifact.TryGetSource" /> is the only way to reach
///         the content, so the check cannot be skipped and cannot be written wrongly — a
///         <c>Source is not null</c> check, for one, is always true and filters nothing.
///     </para>
/// </remarks>
internal static class SourceOutput
{
    /// <summary>Emits the artifact, skipping it when the template produced no content.</summary>
    public static void AddSource(this SourceProductionContext context, Artifact artifact)
    {
        if (!artifact.TryGetSource(out var source))
            return;

        // Roslyn's own two-argument instance method — an instance method always wins over an
        // extension, so this does not recurse into the overload above.
        context.AddSource(artifact.HintName, source);
    }
}
