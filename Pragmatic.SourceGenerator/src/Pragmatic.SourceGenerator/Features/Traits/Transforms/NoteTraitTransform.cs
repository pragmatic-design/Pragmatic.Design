using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Transforms;

/// <summary>
/// Extracts <see cref="NoteTraitModel"/> from an entity annotated with [HasNotes].
/// </summary>
internal static class NoteTraitTransform
{
    public static NoteTraitModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        AttributeData? notesAttr = null;
        foreach (var attr in symbol.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac?.Name == "HasNotesAttribute" &&
                ac.ContainingNamespace?.ToDisplayString() == "Pragmatic.Notes")
            {
                notesAttr = attr;
                break;
            }
        }

        if (notesAttr is null) return null;

        var maxLength = 4000;
        var allowEditing = true;
        var editWindowMinutes = -1;
        string? subBoundaryOverride = null;

        foreach (var namedArg in notesAttr.NamedArguments)
        {
            switch (namedArg.Key)
            {
                case "MaxLength" when namedArg.Value.Value is int ml: maxLength = ml; break;
                case "AllowEditing" when namedArg.Value.Value is bool ae: allowEditing = ae; break;
                case "EditWindowMinutes" when namedArg.Value.Value is int ew: editWindowMinutes = ew; break;
                case "SubBoundary" when namedArg.Value.Value is string sb: subBoundaryOverride = sb; break;
            }
        }

        var idType = Actions.Transforms.EntityTypeHelpers.GetEntityKeyType(symbol, context.SemanticModel.Compilation)?.ToDisplayString();
        if (idType is null) return null;

        var (boundaryFullTypeName, boundaryName) = ResolveBoundary(symbol);
        var (resourceSegment, resourceParamName) = ResolveResource(symbol);

        return new NoteTraitModel
        {
            ParentTypeName = symbol.Name,
            ParentIsOwned = Core.TraitDetector.Detect(symbol).IsOwnedEntity,
            ParentIsScoped = Core.TraitDetector.Detect(symbol).IsScopedEntity,
            ParentIsTenantScoped = Core.TraitDetector.Detect(symbol).IsMultiTenant,
            ParentNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
            ParentFullTypeName = symbol.ToDisplayString(),
            IdType = idType,
            BoundaryFullTypeName = boundaryFullTypeName,
            BoundaryName = boundaryName,
            ResourceSegment = resourceSegment,
            ResourceParamName = resourceParamName,
            MaxLength = maxLength,
            AllowEditing = allowEditing,
            EditWindowMinutes = editWindowMinutes,
            SubBoundaryOverride = subBoundaryOverride,
            LocationInfo = LocationInfo.From(symbol.Locations.Length > 0 ? symbol.Locations[0] : null),
        };
    }

    /// <summary>The entity's boundary — declared, or the single one of its assembly.</summary>
    private static (string? fullTypeName, string? name) ResolveBoundary(INamedTypeSymbol symbol)
        => BoundaryOwnershipReader.BoundaryOf(symbol);

    private static (string? segment, string? paramName) ResolveResource(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != AttributeNames.Resource) continue;
            var segment = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value as string : null;
            string? paramName = null;
            foreach (var namedArg in attr.NamedArguments)
                if (namedArg.Key == "ParamName" && namedArg.Value.Value is string pn) paramName = pn;
            return (segment, paramName);
        }

        return (null, null);
    }
}
