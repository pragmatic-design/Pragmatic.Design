using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Mapping.Models;
using Pragmatic.SourceGenerator.Features.Resource.Models;

namespace Pragmatic.SourceGenerator.Features.Resource.Transforms;

/// <summary>
///     Describes the read DTOs <c>[Resource]</c> generates as mapping models, so the Mapping feature
///     renders their <c>FromEntity</c> and <c>Projection</c> instead of Resource reimplementing them.
/// </summary>
/// <remarks>
///     <para>
///         <c>MappingFeature</c> is syntax-driven: it reads <c>[MapFrom&lt;T&gt;]</c> off a symbol, and a
///         DTO this generator creates has no symbol in the compilation being analysed. So the mapping
///         for a generated DTO was written a second time inside <c>ResourceDtoTemplate</c> — two
///         implementations of "copy these properties across", free to drift.
///     </para>
///     <para>
///         What is shared is the <b>template</b>, not the transform. The transform's job — matching a
///         DTO's members against an entity's, with conversions, nested DTOs and collections — needs
///         both symbols and cannot run for a type that does not exist yet. It is also not needed here:
///         a Resource read DTO is scalars copied by name, and the one exception (<c>Id</c> from
///         <c>PersistenceId</c>) is known.
///     </para>
/// </remarks>
internal static class ResourceMappingModelBuilder
{
    private const int CapRead = 2;
    private const int CapList = 16;

    public static ImmutableArray<MappingModel> Build(ResourceCrudModel model)
    {
        var caps = model.Resource.Capabilities;
        var builder = ImmutableArray.CreateBuilder<MappingModel>();

        // Only for the shapes this generator still writes. A DTO the developer declared with
        // [ReturnsDto<T>] carries its own [MapFrom] and is mapped by the syntax-driven path; describing
        // it here too would render the mapping twice into the same partial.
        if ((caps & CapRead) != 0 && model.NeedsScaffoldedReadDto)
            builder.Add(For(model, model.ScaffoldedReadDto, model.ReadProperties));

        if ((caps & CapList) != 0 && model.NeedsScaffoldedListItemDto)
            builder.Add(For(model, model.ScaffoldedListItemDto, model.ListProperties));

        return builder.ToImmutable();
    }

    private static MappingModel For(
        ResourceCrudModel model, string dtoName, EquatableArray<ResourcePropertyInfo> properties)
    {
        var resource = model.Resource;
        var entityFullName = resource.FullTypeName.StartsWith("global::", System.StringComparison.Ordinal)
            ? resource.FullTypeName.Substring("global::".Length)
            : resource.FullTypeName;

        var props = ImmutableArray.CreateBuilder<PropertyMappingModel>();

        // The identifier, read from the mapped column. `Id` is the generated alias (`Id => PersistenceId`)
        // and is not a column, so a projection reading it would not translate to SQL.
        props.Add(Scalar("Id", SimpleName(resource.IdType), "entity.PersistenceId", isNullable: false));

        foreach (var property in properties)
        {
            if (property.IsPrimaryKey)
                continue;

            props.Add(Scalar(
                property.Name, property.TypeName, $"entity.{property.Name}", property.IsNullable));
        }

        return new MappingModel
        {
            Namespace = resource.Namespace,
            TypeName = dtoName,
            Accessibility = "public",
            TypeKind = "record",
            IsRecord = true,
            SourceTypeFullName = entityFullName,
            SourceTypeName = resource.TypeName,
            HasMapFrom = true,
            // A read DTO is read-only: there is no ToEntity to generate, and the entity's setters are
            // the generated Set{Name} ones anyway.
            HasMapTo = false,
            GenerateProjection = true,
            Properties = props.ToImmutable(),
        };
    }

    /// <summary>
    ///     A property copied straight across: no conversion, no nesting, translatable to SQL.
    /// </summary>
    private static PropertyMappingModel Scalar(
        string name, string type, string sourceExpression, bool isNullable)
        => new()
        {
            PropertyName = name,
            PropertyType = type,
            IsNullable = isNullable,
            IsInitOnly = true,
            Resolution = MappingResolution.DirectMatch,
            SourceExpression = sourceExpression,
            SourcePropertyType = type,
            SourceIsNullable = isNullable,
            IsSqlTranslatable = true,
        };

    private static string SimpleName(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName : typeName.Substring(dot + 1);
    }
}
