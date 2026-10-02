using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Serialization.Models;

/// <summary>
///     One boundary root's contribution: the transitive closure of object types reachable from the
///     root (the root plus every nested DTO) plus the leaf types their properties reference.
///     Value-equatable so it caches in the incremental pipeline.
/// </summary>
internal sealed record JsonRootContribution(
    EquatableArray<JsonObjectModel> Objects,
    EquatableArray<JsonLeafModel> Leaves,
    EquatableArray<JsonCollectionModel> Collections)
{
    /// <summary>
    ///     The two contributions together, or whichever one is not <c>null</c>. Duplicates are fine —
    ///     the context model keys everything by type expression before emitting.
    /// </summary>
    public static JsonRootContribution? Merge(JsonRootContribution? first, JsonRootContribution? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;

        return new JsonRootContribution(
            first.Objects.AsImmutableArray().AddRange(second.Objects.AsImmutableArray()),
            first.Leaves.AsImmutableArray().AddRange(second.Leaves.AsImmutableArray()),
            first.Collections.AsImmutableArray().AddRange(second.Collections.AsImmutableArray()));
    }
}
