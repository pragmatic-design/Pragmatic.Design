using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Transforms;

/// <summary>
/// Extracts <see cref="TagTraitModel"/> from an entity annotated with [HasTags].
/// Reads entity metadata (IdType, BoundaryName) directly from the symbol.
/// </summary>
internal static class TagTraitTransform
{
    public static TagTraitModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        // Find [HasTags] attribute
        AttributeData? tagsAttr = null;
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == AttributeNames.HasTags)
            {
                tagsAttr = attr;
                break;
            }
        }

        if (tagsAttr is null) return null;

        // Extract options
        var maxPerEntity = 50;
        var allowCustom = true;
        var caseSensitive = false;
        string? scopeOverride = null;
        string? subBoundaryOverride = null;

        foreach (var namedArg in tagsAttr.NamedArguments)
        {
            switch (namedArg.Key)
            {
                case "MaxPerEntity" when namedArg.Value.Value is int mpe:
                    maxPerEntity = mpe;
                    break;
                case "AllowCustom" when namedArg.Value.Value is bool ac:
                    allowCustom = ac;
                    break;
                case "CaseSensitive" when namedArg.Value.Value is bool cs:
                    caseSensitive = cs;
                    break;
                case "Scope" when namedArg.Value.Value is string s:
                    scopeOverride = s;
                    break;
                case "SubBoundary" when namedArg.Value.Value is string sb:
                    subBoundaryOverride = sb;
                    break;
            }
        }

        var idType = Actions.Transforms.EntityTypeHelpers.GetEntityKeyType(symbol, context.SemanticModel.Compilation)?.ToDisplayString();
        if (idType is null) return null;

        // Resolve boundary from [BelongsTo<T>]
        var (boundaryFullTypeName, boundaryName) = ResolveBoundary(symbol);

        // Resolve resource info from [Resource("segment")]
        var (resourceSegment, resourceParamName) = ResolveResource(symbol);

        return new TagTraitModel
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
            MaxPerEntity = maxPerEntity,
            AllowCustom = allowCustom,
            CaseSensitive = caseSensitive,
            ScopeOverride = scopeOverride,
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
            if (attr.AttributeClass?.ToDisplayString() != AttributeNames.Resource)
                continue;

            var segment = attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value as string
                : null;

            string? paramName = null;
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "ParamName" && namedArg.Value.Value is string pn)
                    paramName = pn;
            }

            return (segment, paramName);
        }

        return (null, null);
    }
}
