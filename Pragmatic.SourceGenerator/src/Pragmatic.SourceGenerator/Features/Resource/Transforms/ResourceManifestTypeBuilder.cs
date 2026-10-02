using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Manifest.Models;
using Pragmatic.SourceGenerator.Features.Resource.Models;

namespace Pragmatic.SourceGenerator.Features.Resource.Transforms;

/// <summary>
///     Describes the four DTOs <c>[Resource]</c> generates (Create / Read / Update / ListItem) for the API
///     manifest.
///     <para>
///         The manifest resolves a type by looking up its symbol, which cannot work for a type this generator
///         is creating. Contributing the description directly is what keeps a generated client from exposing
///         these as <c>object</c>. The property shapes come from the same model the templates render, so the
///         two cannot drift.
///     </para>
/// </summary>
internal static class ResourceManifestTypeBuilder
{
    public static ImmutableArray<ManifestTypeModel> Build(ImmutableArray<ResourceCrudModel> models)
    {
        if (models.IsDefaultOrEmpty)
            return ImmutableArray<ManifestTypeModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<ManifestTypeModel>();
        foreach (var model in models)
        {
            var ns = model.Resource.Namespace;

            // Only the read shapes. The create/update input DTOs are gone: a mutation IS the input
            // contract — its own properties are the request body — so a separate type described
            // nothing and still had to be carried through the manifest and every JSON context.
            //
            // And only the ones this generator writes. A DTO declared with [ReturnsDto<T>] is the
            // developer's own source, so the manifest resolves its symbol the ordinary way; describing
            // it from here would put it in twice, once from a model and once from the compilation.
            if (model.NeedsScaffoldedReadDto)
                Add(builder, ns, model.ScaffoldedReadDto, model.ReadProperties, patchSemantics: false);
            if (model.NeedsScaffoldedListItemDto)
                Add(builder, ns, model.ScaffoldedListItemDto, model.ListProperties, patchSemantics: false);
        }

        return builder.ToImmutable();
    }

    private static void Add(
        ImmutableArray<ManifestTypeModel>.Builder builder,
        string @namespace,
        string dtoName,
        EquatableArray<ResourcePropertyInfo> properties,
        bool patchSemantics)
    {
        if (properties.Length == 0)
            return;

        var props = ImmutableArray.CreateBuilder<ManifestPropertyModel>();
        foreach (var property in properties)
        {
            // The Update DTO makes everything optional (patch semantics), exactly as ResourceDtoTemplate does.
            var isNullable = patchSemantics || property.IsNullable;

            props.Add(new ManifestPropertyModel
            {
                Name = property.Name,
                Type = property.TypeName.TrimEnd('?'),
                IsRequired = !patchSemantics && property.IsRequired,
                IsNullable = isNullable
            });
        }

        builder.Add(new ManifestTypeModel
        {
            Type = $"global::{@namespace}.{dtoName}",
            SimpleName = dtoName,
            Kind = ManifestTypeKind.Dto,
            Properties = new EquatableArray<ManifestPropertyModel>(props.ToImmutable())
        });
    }
}
