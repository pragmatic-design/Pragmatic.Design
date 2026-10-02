using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Validation.Models;

/// <summary>
///     One cross-property dependency edge for change-tracking-aware validation: when <see cref="Property"/>
///     is modified, the <see cref="Dependents"/> properties must be re-validated. Value-equatable (replaces a
///     raw <c>ImmutableDictionary&lt;string, ImmutableArray&lt;string&gt;&gt;</c>) so the owning
///     <see cref="ValidatableModel"/> stays cacheable in the incremental pipeline.
/// </summary>
internal sealed record PropertyDependencyModel
{
    /// <summary>The property whose modification triggers re-validation of <see cref="Dependents"/>.</summary>
    public required string Property { get; init; }

    /// <summary>Properties that depend on <see cref="Property"/> and must be re-validated when it changes.</summary>
    public EquatableArray<string> Dependents { get; init; } = EquatableArray<string>.Empty;
}
