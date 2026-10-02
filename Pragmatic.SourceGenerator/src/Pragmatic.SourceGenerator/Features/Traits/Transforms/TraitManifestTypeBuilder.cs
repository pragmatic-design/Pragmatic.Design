using System.Collections.Generic;
using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Manifest.Models;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Transforms;

/// <summary>
///     Describes the read DTOs the trait feature generates, for the API manifest.
///     <para>
///         The manifest normally discovers a type by resolving its symbol in the compilation. That cannot work
///         for a type the generator is itself creating: it does not exist yet when the manifest is built. So
///         these are contributed explicitly, from the same <see cref="TraitDtoShape"/> the templates render —
///         which is what keeps the description and the emitted type from drifting apart.
///     </para>
/// </summary>
internal static class TraitManifestTypeBuilder
{
    public static ImmutableArray<ManifestTypeModel> BuildCommentDtos(ImmutableArray<CommentTraitModel> models)
    {
        var builder = ImmutableArray.CreateBuilder<ManifestTypeModel>();
        foreach (var model in models)
        {
            var name = $"{model.CommentTypeName}Dto";
            builder.Add(Dto($"global::{model.ParentNamespace}.{name}", name,
                model.BoundaryName, TraitDtoShape.Comment(model)));
        }

        return builder.ToImmutable();
    }

    public static ImmutableArray<ManifestTypeModel> BuildNoteDtos(ImmutableArray<NoteTraitModel> models)
    {
        var builder = ImmutableArray.CreateBuilder<ManifestTypeModel>();
        foreach (var model in models)
        {
            var name = $"{model.NoteTypeName}Dto";
            builder.Add(Dto($"global::{model.ParentNamespace}.{name}", name,
                model.BoundaryName, TraitDtoShape.Note(model)));
        }

        return builder.ToImmutable();
    }

    public static ImmutableArray<ManifestTypeModel> BuildTagDtos(ImmutableArray<TagTraitModel> models)
    {
        var builder = ImmutableArray.CreateBuilder<ManifestTypeModel>();
        foreach (var model in models)
        {
            var name = model.TagDtoTypeName;
            builder.Add(Dto($"global::{model.ParentNamespace}.{name}", name,
                model.BoundaryName, TraitDtoShape.Tag(model)));
        }

        return builder.ToImmutable();
    }

    public static ImmutableArray<ManifestTypeModel> BuildAttachmentDtos(ImmutableArray<AttachmentTraitModel> models)
    {
        var builder = ImmutableArray.CreateBuilder<ManifestTypeModel>();
        foreach (var model in models)
        {
            var name = $"{model.AttachmentTypeName}Dto";
            builder.Add(Dto($"global::{model.ParentNamespace}.{name}", name,
                model.BoundaryName, TraitDtoShape.Attachment(model)));
        }

        return builder.ToImmutable();
    }

    private static ManifestTypeModel Dto(
        string fullTypeName, string simpleName, string? boundary, IReadOnlyList<TraitDtoProperty> properties)
    {
        var props = ImmutableArray.CreateBuilder<ManifestPropertyModel>();
        foreach (var property in properties)
        {
            props.Add(new ManifestPropertyModel
            {
                Name = property.Name,
                Type = property.Type.TrimEnd('?'),
                IsRequired = property.IsRequired,
                IsNullable = property.IsNullable,
                IsEnum = property.IsEnum
            });
        }

        return new ManifestTypeModel
        {
            Type = fullTypeName,
            SimpleName = simpleName,
            Kind = ManifestTypeKind.Dto,
            Boundary = boundary,
            Properties = new EquatableArray<ManifestPropertyModel>(props.ToImmutable())
        };
    }
}
