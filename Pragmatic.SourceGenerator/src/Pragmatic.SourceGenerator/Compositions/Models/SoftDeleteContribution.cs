using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Compositions.Models;

/// <summary>
///     Contribution for soft-delete behavior on mutations.
///     Detected from [SoftDelete] on entity or explicit SoftDelete=true on [Mutation].
/// </summary>
internal sealed record SoftDeleteContribution
{
    /// <summary>Whether cascade soft-delete is enabled ([SoftDelete(Cascade = true)]).</summary>
    public bool Cascade { get; init; }

    /// <summary>Navigation properties to cascade soft-delete to. EquatableArray so this model (embedded in
    /// MutationModel, which flows through the incremental pipeline) stays value-equatable for caching.</summary>
    public EquatableArray<SoftDeleteCascadeTargetModel> CascadeTargets { get; init; }
        = EquatableArray<SoftDeleteCascadeTargetModel>.Empty;

    /// <summary>Whether there are navigation targets to cascade soft-delete.</summary>
    public bool HasCascadeTargets => Cascade && !CascadeTargets.IsDefaultOrEmpty;

    /// <summary>Whether this mutation is a Restore (reset soft-delete fields).</summary>
    public bool IsRestore { get; init; }
}
