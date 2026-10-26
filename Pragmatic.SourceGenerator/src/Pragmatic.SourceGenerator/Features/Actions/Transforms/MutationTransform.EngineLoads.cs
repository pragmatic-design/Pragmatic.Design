using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     The navigations a mutation's load reads on an engine that compiles its reads, known at build: what EF Core's load includes, where it
///     reads part of the list from generated statics at run time (<c>WrittenNavigations</c>, <c>RequiredNavigations</c>).
/// </summary>
internal static partial class MutationTransform
{
    /// <summary>A child mutation nests another past this depth only in a cycle: the walk stops there rather than looping.</summary>
    private const int MaxWrittenDepth = 8;

    /// <summary>
    ///     What <see cref="MutationModel.EngineLoadPaths" /> says, in the order the EF Core load includes them, each once: the <c>[EagerLoad]</c>
    ///     paths; the targets of a soft delete that cascades; the collections the mutation writes and, through its child mutations, the ones
    ///     they write in turn; the ones its <c>[LinkIds]</c> lists choose the rows of; then what the response DTO maps from while the mutation
    ///     answers with it.
    /// </summary>
    private static EquatableArray<string> EngineLoadPaths(MutationModel model, INamedTypeSymbol mutation, INamedTypeSymbol? response, Compilation compilation, CancellationToken ct)
    {
        var paths = model.Includes.ToList();
        void Add(string path)
        {
            if (!paths.Contains(path))
                paths.Add(path);
        }

        if ((model.IsDelete || model.IsRestore) && model.SoftDelete is { HasCascadeTargets: true } softDelete)
        {
            foreach (var target in softDelete.CascadeTargets)
                Add(target.PropertyName);
        }

        foreach (var path in WrittenCollections(mutation, model.Children, compilation, 0))
            Add(path);

        // The collections a [LinkIds] list chooses the rows of: a link of keys decides what to remove by what is there, so the collection is read
        // with the entity, as EF Core's linker loads it first.
        foreach (var link in model.Links)
        {
            if (link.Strategy != "Ignore")
                Add(link.Navigation);
        }

        if (model.EffectiveReturnType == MutationReturnTypeValue.Entity && model.ResponseDtoFullTypeName is not null && response is not null)
        {
            foreach (var navigation in Mapping.Transforms.MappingTransform.RequiredNavigationsOf(response, compilation, ct))
                Add(navigation);
        }

        return paths.ToEquatableArray();
    }

    /// <summary>
    ///     The collections a mutation writes (a keyed merge decides what to remove by what was loaded), and below each child mutation the ones it
    ///     writes, prefixed: the paths the child mutations' <c>WrittenNavigations</c> list at run time. A child written as one reference is not
    ///     among them: the engines refuse it (PRAG3037, PRAG3112).
    /// </summary>
    private static IEnumerable<string> WrittenCollections(INamedTypeSymbol mutation, IEnumerable<MutationChildModel> children, Compilation compilation, int depth)
    {
        foreach (var child in children)
        {
            if (!child.IsWritable || !child.IsCollection)
                continue;
            yield return child.TargetPropertyName;
            if (!child.IsChildMutation || depth >= MaxWrittenDepth)
                continue;
            var property = mutation.GetMembers(child.PropertyName).OfType<IPropertySymbol>().FirstOrDefault();
            if (property is null || MutationChildAnalyzer.UnwrapChildDto(property.Type).Dto is not { } childMutation
                || MutationChildAnalyzer.MutationEntityOf(childMutation) is not { } childEntity)
                continue;
            var grandchildren = MatchProperties(childMutation, childEntity, compilation).Children;
            foreach (var path in WrittenCollections(childMutation, grandchildren, compilation, depth + 1))
                yield return child.TargetPropertyName + "." + path;
        }
    }
}
