// Pragmatic.SourceGen.Testing - Incremental run result
// This file is linked into test projects, not compiled directly.

using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGen.Testing;

/// <summary>
///     Wraps the second run of an incremental generator (with tracked steps enabled) so tests can assert
///     which pipeline steps re-executed.
/// </summary>
public sealed class IncrementalRunResult
{
    public IncrementalRunResult(GeneratorRunResult runResult) => RunResult = runResult;

    /// <summary>The Roslyn run result of the second (incremental) run, with <c>TrackedSteps</c> populated.</summary>
    public GeneratorRunResult RunResult { get; }
}
