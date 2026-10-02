using Pragmatic.SourceGen;

namespace Pragmatic.Testing.SourceGenerator.Models;

/// <summary>
///     One published operation and what the generator emitted for it — including the contracts it
///     considered and declined, with the reason.
/// </summary>
/// <remarks>
///     ⚠️ A generated suite reports what it wrote and never what it skipped, so "eight operations are
///     covered" and "the application publishes eight operations" read the same from inside it. Comparing
///     generated names against a route table by hand is the alternative, and it cannot see reasons.
/// </remarks>
internal sealed record ContractCoverageModel
{
    public required string Boundary { get; init; }
    public required string Operation { get; init; }
    public required string HttpMethod { get; init; }
    public required string Route { get; init; }

    /// <summary>What was emitted: <c>auth</c>, <c>not-found</c>, <c>create</c>, <c>validation</c>, <c>isolation</c>, <c>transition</c>.</summary>
    public required EquatableArray<string> Contracts { get; init; }

    /// <summary>One sentence per contract the generator considered and did not emit.</summary>
    public required EquatableArray<string> NotCovered { get; init; }
}
