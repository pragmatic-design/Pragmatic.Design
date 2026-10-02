using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Resource.Models;

namespace Pragmatic.SourceGenerator.Features.Resource.Transforms;

/// <summary>
/// Extracts <see cref="ResourceModel"/> from a [Resource]-annotated entity class.
/// </summary>
internal static class ResourceTransform
{
    public static ResourceModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        // Find the [Resource("segment")] attribute
        AttributeData? resourceAttr = null;
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == AttributeNames.Resource)
            {
                resourceAttr = attr;
                break;
            }
        }

        if (resourceAttr is null) return null;

        // Constructor arg: segment
        var segment = resourceAttr.ConstructorArguments.Length > 0
            ? resourceAttr.ConstructorArguments[0].Value as string
            : null;

        if (string.IsNullOrWhiteSpace(segment)) return null;

        // Named args
        var capabilities = 0;
        string? paramName = null;

        foreach (var namedArg in resourceAttr.NamedArguments)
        {
            switch (namedArg.Key)
            {
                case "Capabilities" when namedArg.Value.Value is int cap:
                    capabilities = cap;
                    break;
                case "ParamName" when namedArg.Value.Value is string pn:
                    paramName = pn;
                    break;
            }
        }

        var idType = Actions.Transforms.EntityTypeHelpers.GetEntityKeyType(symbol, context.SemanticModel.Compilation)?.ToDisplayString();

        // Resolve boundary from [BelongsTo<T>]
        var (boundaryFullTypeName, boundaryName) = ResolveBoundary(symbol);

        // Reuses the read that Actions already does rather than writing a second one: one attribute
        // read from two places is how the two drift apart.
        var partOfParent = Actions.Transforms.MutationChildAnalyzer.PartOfParentOf(symbol);

        return new ResourceModel
        {
            Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
            TypeName = symbol.Name,
            FullTypeName = $"global::{symbol.ToDisplayString()}",
            Segment = segment!,
            Capabilities = capabilities,
            ParamName = paramName,
            BoundaryFullTypeName = boundaryFullTypeName,
            BoundaryName = boundaryName,
            IdType = idType ?? "System.Guid",
            PartOfParentTypeName = partOfParent,
            HasRouteConsumingTrait = HasRouteConsumingTrait(symbol),
            LocationInfo = LocationInfo.From(symbol.Locations.Length > 0 ? symbol.Locations[0] : null),
        };
    }

    /// <summary>
    ///     Whether one of the traits that hang their endpoints under this segment is declared here.
    /// </summary>
    /// <remarks>
    ///     Read from the type rather than from the trait pipeline: the two run independently, and a
    ///     diagnostic that waited for the other feature's output would be asking one generator to see
    ///     another's.
    /// </remarks>
    private static bool HasRouteConsumingTrait(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            switch (attr.AttributeClass?.ToDisplayString())
            {
                case AttributeNames.HasComments:
                case AttributeNames.HasTags:
                case AttributeNames.HasNotes:
                case AttributeNames.HasAttachments:
                    return true;
            }
        }

        return false;
    }

    /// <summary>The entity's boundary — declared, or the single one of its assembly.</summary>
    /// <summary>
    ///     The boundary this resource belongs to: fully qualified for the model, short for the permission
    ///     prefix. Qualified here rather than at each consumer — four of them needed it and one forgot.
    /// </summary>
    private static (string? fullTypeName, string? name) ResolveBoundary(INamedTypeSymbol symbol)
        => (BoundaryOwnershipReader.QualifiedBoundaryOf(symbol), BoundaryOwnershipReader.BoundaryOf(symbol).ShortName);
}
