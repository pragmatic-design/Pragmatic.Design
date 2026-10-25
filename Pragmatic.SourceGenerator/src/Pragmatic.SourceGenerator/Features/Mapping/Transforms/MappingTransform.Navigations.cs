using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Mapping.Transforms;

/// <summary>
///     A DTO's navigations asked from outside Mapping, as its own transform computes them: what an engine that compiles its reads needs to
///     know at build, where EF Core's invoker reads the generated static at run time.
/// </summary>
internal static partial class MappingTransform
{
    /// <summary>
    ///     The <c>RequiredNavigations</c> <paramref name="dto" /> publishes: the walk <see cref="TransformMapFrom" /> makes, over the same
    ///     properties and depth. Empty for a DTO that declares no <c>[MapFrom]</c>.
    /// </summary>
    public static ImmutableArray<string> RequiredNavigationsOf(INamedTypeSymbol dto, Compilation compilation, CancellationToken ct)
    {
        if (MapFromSourceOf(dto) is not { } source)
            return ImmutableArray<string>.Empty;

        var depth = ProjectionMaxDepthOf(dto);
        return ComputeRequiredNavigationsDeep(
            ExtractPropertyMappings(dto, source, compilation, ct, depth),
            compilation,
            ct,
            depth,
            new HashSet<string>(StringComparer.Ordinal) { dto.ToDisplayString() });
    }

    /// <summary>The <c>MaxDepth</c> of a DTO's <c>[GenerateProjection]</c>, 5 without one: the cap that flows into nested projection inlining.</summary>
    private static int ProjectionMaxDepthOf(INamedTypeSymbol dto)
    {
        var projectionMaxDepth = 5;
        var projectionAttr = dto.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Mapping.Attributes.GenerateProjectionAttribute");
        if (projectionAttr is not null)
            foreach (var na in projectionAttr.NamedArguments)
                if (na is { Key: "MaxDepth", Value.Value: int md and > 0 })
                    projectionMaxDepth = md;
        return projectionMaxDepth;
    }
}
