using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Resource.Models;

namespace Pragmatic.SourceGenerator.Features.Resource.Templates;

/// <summary>
/// Generates sealed record DTOs for a [Resource(Capabilities)] entity.
/// One file per DTO kind: CreateDto, ReadDto, UpdateDto, ListItemDto.
/// </summary>
internal sealed class ResourceDtoTemplate : CSharpTemplate
{
    private readonly ResourceCrudModel _model;
    private readonly ResourceDtoKind _kind;
    private readonly string _dtoName;

    public ResourceDtoTemplate(ResourceCrudModel model, ResourceDtoKind kind)
    {
        _model = model;
        _kind = kind;
        _dtoName = kind switch
        {
            ResourceDtoKind.Create => $"{model.Resource.TypeName}CreateDto",
            ResourceDtoKind.Read => $"{model.Resource.TypeName}ReadDto",
            ResourceDtoKind.Update => $"{model.Resource.TypeName}UpdateDto",
            ResourceDtoKind.ListItem => $"{model.Resource.TypeName}ListItemDto",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Resource";
    protected override string? TriggerInfo => $"[Resource] {_kind}Dto for {_model.Resource.TypeName}";

    public override Artifact RenderOutput()
        => new($"_Resource.{_model.Resource.TypeName}.{_kind}Dto.g.cs", ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.Resource.Namespace);
        AppendLine();

        XmlSummary($"SG-generated {_kind} DTO for <see cref=\"{_model.Resource.TypeName}\"/>.");
        AppendLine($"public sealed partial record {_dtoName}");
        AppendLine("{");
        IncreaseIndent();

        var properties = _kind switch
        {
            ResourceDtoKind.Create => _model.CreateProperties,
            ResourceDtoKind.Read => _model.ReadProperties,
            ResourceDtoKind.Update => _model.UpdateProperties,
            ResourceDtoKind.ListItem => _model.ListProperties,
            _ => ImmutableArray<ResourcePropertyInfo>.Empty
        };

        // The identifier is emitted below from the resource's declared id type, so the entity's own
        // must not be emitted again. It usually is not there — `Id => PersistenceId` lives in a partial
        // this generator writes, invisible to the property walk — but an entity that declares its own
        // Id puts it in the list, and then the DTO had the property twice and the mapping assigned it
        // twice (CS1912). Only a snapshot with such an entity showed it; the Showcase cannot.
        if (_kind is ResourceDtoKind.Read or ResourceDtoKind.ListItem)
            properties = properties.Where(p => !p.IsPrimaryKey).ToImmutableArray();

        // The identifier, on the shapes that represent a resource rather than describe a change.
        //
        // It is added here rather than picked up from the entity because the entity does not appear to
        // have one: `Id => PersistenceId` is declared in a partial this generator emits, so it does not
        // exist in the compilation being analysed and the property walk never sees it. The result was a
        // read DTO with no identifier at all — a representation a client cannot address, and one that
        // made a 201 useless the moment the create started answering with it.
        if (_kind is ResourceDtoKind.Read or ResourceDtoKind.ListItem)
            AppendLine($"public required {IdTypeName} Id {{ get; init; }}");

        foreach (var prop in properties)
        {
            var typeName = prop.TypeName;
            var required = "";

            if (_kind == ResourceDtoKind.Update)
            {
                // Update DTO: all properties nullable (patch semantics)
                if (!prop.IsNullable)
                    typeName += "?";
            }
            else if (prop.IsRequired || (!prop.IsNullable && typeName == "string"))
            {
                // Non-nullable reference types need required on records
                required = "required ";
            }

            AppendLine($"public {required}{typeName} {prop.Name} {{ get; init; }}");
        }

        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>The resource's id type, simple-named for use inside its own namespace.</summary>
    private string IdTypeName
    {
        get
        {
            var idType = _model.Resource.IdType;
            var dot = idType.LastIndexOf('.');
            return dot < 0 ? idType : idType.Substring(dot + 1);
        }
    }
}

internal enum ResourceDtoKind
{
    Create,
    Read,
    Update,
    ListItem,
}
