using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Transforms;

/// <summary>
/// Extracts <see cref="AttachmentTraitModel"/> from an entity annotated with [HasAttachments].
/// </summary>
internal static class AttachmentTraitTransform
{
    public static AttachmentTraitModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        AttributeData? attr = null;
        foreach (var a in symbol.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() == AttributeNames.HasAttachments)
            {
                attr = a;
                break;
            }
        }

        if (attr is null) return null;

        var maxPerEntity = 20;
        long maxFileSizeBytes = 10_485_760;
        var allowedExtensions = "";
        string? container = null;
        string? subBoundary = null;
        var purgeAfterDays = 0;
        var purgeCron = "0 3 * * *";
        var thumbWidth = 0;
        var thumbHeight = 0;

        foreach (var namedArg in attr.NamedArguments)
        {
            switch (namedArg.Key)
            {
                case "MaxPerEntity" when namedArg.Value.Value is int mpe: maxPerEntity = mpe; break;
                case "MaxFileSizeBytes" when namedArg.Value.Value is long mfs: maxFileSizeBytes = mfs; break;
                case "AllowedExtensions" when namedArg.Value.Value is string ae: allowedExtensions = ae; break;
                case "Container" when namedArg.Value.Value is string c: container = c; break;
                case "SubBoundary" when namedArg.Value.Value is string sb: subBoundary = sb; break;
                case "PurgeDeletedAfterDays" when namedArg.Value.Value is int pd: purgeAfterDays = pd; break;
                case "PurgeCron" when namedArg.Value.Value is string pc: purgeCron = pc; break;
                case "ThumbnailMaxWidth" when namedArg.Value.Value is int tw: thumbWidth = tw; break;
                case "ThumbnailMaxHeight" when namedArg.Value.Value is int th: thumbHeight = th; break;
            }
        }

        var idType = Actions.Transforms.EntityTypeHelpers.GetEntityKeyType(symbol, context.SemanticModel.Compilation)?.ToDisplayString();
        if (idType is null) return null;

        var (boundaryFullTypeName, boundaryName) = ResolveBoundary(symbol);
        var (resourceSegment, resourceParamName) = ResolveResource(symbol);

        return new AttachmentTraitModel
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
            MaxFileSizeBytes = maxFileSizeBytes,
            AllowedExtensions = allowedExtensions,
            ContainerOverride = container,
            SubBoundaryOverride = subBoundary,
            PurgeDeletedAfterDays = purgeAfterDays,
            PurgeCron = purgeCron,
            ThumbnailMaxWidth = thumbWidth,
            ThumbnailMaxHeight = thumbHeight,
            LocationInfo = LocationInfo.From(symbol.Locations.Length > 0 ? symbol.Locations[0] : null),
        };
    }

    /// <summary>The entity's boundary — declared, or the single one of its assembly.</summary>
    private static (string? fullTypeName, string? name) ResolveBoundary(INamedTypeSymbol symbol)
        => BoundaryOwnershipReader.BoundaryOf(symbol);

    private static (string? segment, string? paramName) ResolveResource(INamedTypeSymbol symbol)
    {
        foreach (var a in symbol.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() != AttributeNames.Resource) continue;
            var segment = a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null;
            string? pn = null;
            foreach (var na in a.NamedArguments)
                if (na.Key == "ParamName" && na.Value.Value is string p) pn = p;
            return (segment, pn);
        }
        return (null, null);
    }
}
