using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Manifest.Models;
using Pragmatic.SourceGenerator.Features.Patch.Models;

namespace Pragmatic.SourceGenerator.Features.Patch.Transforms;

/// <summary>
///     Describes the patch DTOs <c>[GeneratePatch&lt;TEntity&gt;]</c> generates, for the API manifest.
///     <para>
///         The user writes only <c>public partial record FooPatchDto;</c> — every property is generated. So
///         the manifest, which describes a type by resolving its symbol, found the type but no properties and
///         dropped it; a generated client could then only expose it as <c>object</c> (PRAG2301). The shape is
///         contributed from the same <see cref="PatchModel" /> that <c>PatchTypeTemplate</c> renders, so the
///         description and the emitted code cannot drift.
///     </para>
///     <para>
///         The template emits <c>Optional&lt;T&gt;</c> properties, but the manifest describes the wire shape,
///         not the CLR one: on the wire an <c>Optional&lt;T&gt;</c> is a <c>T</c> that may be absent. Hence
///         every property is reported as the underlying <c>T</c>, nullable and never required — the same
///         patch semantics <c>ResourceManifestTypeBuilder</c> applies to the Update DTO.
///     </para>
/// </summary>
internal static class PatchManifestTypeBuilder
{
    public static ImmutableArray<ManifestTypeModel> Build(ImmutableArray<PatchModel> models)
    {
        if (models.IsDefaultOrEmpty)
            return ImmutableArray<ManifestTypeModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<ManifestTypeModel>();

        foreach (var model in models)
        {
            if (!model.IsValid || model.Properties.Length == 0)
                continue;

            var props = ImmutableArray.CreateBuilder<ManifestPropertyModel>();
            foreach (var property in model.Properties)
                props.Add(new ManifestPropertyModel
                {
                    Name = property.Name,
                    Type = property.TypeFullName.TrimEnd('?'),
                    IsNullable = true,
                    IsRequired = false
                });

            builder.Add(new ManifestTypeModel
            {
                Type = string.IsNullOrEmpty(model.Namespace)
                    ? $"global::{model.TypeName}"
                    : $"global::{model.Namespace}.{model.TypeName}",
                SimpleName = model.TypeName,
                Kind = ManifestTypeKind.Dto,
                Properties = new EquatableArray<ManifestPropertyModel>(props.ToImmutable())
            });
        }

        return builder.ToImmutable();
    }
}
