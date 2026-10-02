// Pragmatic.SourceGen.Testing - Generator Run Result
// This file is linked into test projects, not compiled directly.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGen.Testing;

/// <summary>
///     Result of running a source generator.
///     Named SourceGenRunResult to avoid conflict with Microsoft.CodeAnalysis.GeneratorRunResult.
/// </summary>
public sealed record SourceGenRunResult(
    GeneratorDriverRunResult RunResult,
    Compilation OutputCompilation,
    ImmutableArray<Diagnostic> Diagnostics)
{
    /// <summary>
    ///     Gets all generated trees.
    /// </summary>
    public ImmutableArray<SyntaxTree> GeneratedTrees => RunResult.GeneratedTrees;

    /// <summary>
    ///     Indicates whether there are any generated files.
    /// </summary>
    public bool HasGeneratedFiles => GeneratedTrees.Length > 0;
}