using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     A [Relation.*] rule violation found while reading an entity, carried through the incremental
///     pipeline as plain values and turned into a real <c>Diagnostic</c> at output time.
/// </summary>
/// <remarks>
///     Validation needs symbols (the target type's members), which only exist during the transform;
///     reporting happens in the source-output stage. Holding a <c>Diagnostic</c> (or a raw
///     <c>Location</c>) in a cached model would pin its <c>SyntaxTree</c> to a dead compilation, hence
///     <see cref="Core.LocationInfo" /> and an <see cref="EquatableArray{T}" /> of message arguments.
/// </remarks>
internal sealed record RelationDiagnosticModel
{
    /// <summary>Which rule was violated.</summary>
    public required RelationDiagnosticKind Kind { get; init; }

    /// <summary>Position of the offending <c>[Relation.*]</c> attribute, when it has one.</summary>
    public LocationInfo? Location { get; init; }

    /// <summary>Message format arguments, in descriptor order.</summary>
    public required EquatableArray<string> Arguments { get; init; }
}
