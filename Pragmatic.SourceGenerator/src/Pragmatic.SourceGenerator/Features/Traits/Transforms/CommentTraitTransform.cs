using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Transforms;

/// <summary>
/// Extracts <see cref="CommentTraitModel"/> from an entity annotated with [HasComments].
/// Reads entity metadata (IdType, BoundaryName) directly from the symbol.
/// </summary>
internal static class CommentTraitTransform
{
    public static CommentTraitModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        // Find [HasComments] attribute
        AttributeData? commentsAttr = null;
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == AttributeNames.HasComments)
            {
                commentsAttr = attr;
                break;
            }
        }

        if (commentsAttr is null) return null;

        // Extract options
        var maxLength = 2000;
        var allowReplies = true;
        var allowEditing = true;
        var editWindowMinutes = -1;
        var requireApproval = false;
        var supportInternalNotes = false;
        string? subBoundaryOverride = null;

        foreach (var namedArg in commentsAttr.NamedArguments)
        {
            switch (namedArg.Key)
            {
                case "MaxLength" when namedArg.Value.Value is int ml:
                    maxLength = ml;
                    break;
                case "AllowReplies" when namedArg.Value.Value is bool ar:
                    allowReplies = ar;
                    break;
                case "AllowEditing" when namedArg.Value.Value is bool ae:
                    allowEditing = ae;
                    break;
                case "EditWindowMinutes" when namedArg.Value.Value is int ew:
                    editWindowMinutes = ew;
                    break;
                case "RequireApproval" when namedArg.Value.Value is bool ra:
                    requireApproval = ra;
                    break;
                case "SupportInternalNotes" when namedArg.Value.Value is bool sin:
                    supportInternalNotes = sin;
                    break;
                case "SubBoundary" when namedArg.Value.Value is string sb:
                    subBoundaryOverride = sb;
                    break;
            }
        }

        // Not an entity: MissingEntityTraitTransform reports it, asking the same function.
        var idType = Actions.Transforms.EntityTypeHelpers.GetEntityKeyType(symbol, context.SemanticModel.Compilation)?.ToDisplayString();
        if (idType is null) return null;

        // Resolve boundary from [BelongsTo<T>]
        var (boundaryFullTypeName, boundaryName) = ResolveBoundary(symbol);

        // Resolve resource info from [Resource("segment")]
        var (resourceSegment, resourceParamName) = ResolveResource(symbol);

        return new CommentTraitModel
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
            AllowReplies = allowReplies,
            AllowEditing = allowEditing,
            EditWindowMinutes = editWindowMinutes,
            RequireApproval = requireApproval,
            SupportInternalNotes = supportInternalNotes,
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
