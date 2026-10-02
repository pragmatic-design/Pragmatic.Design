using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Helper methods for constructor analysis.
/// </summary>
internal static class ConstructorAnalyzer
{
    /// <summary>
    ///     Extracts constructor information from an entity type.
    /// </summary>
    public static (ImmutableArray<ConstructorParameterModel> parameters, bool hasExplicit)
        ExtractConstructorInfo(INamedTypeSymbol entityType, INamedTypeSymbol dtoType)
    {
        var constructors = entityType.Constructors
            .Where(c => !c.IsStatic && c.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        if (constructors.Count == 0)
            return (ImmutableArray<ConstructorParameterModel>.Empty, false);

        // Check for [MapConstructor]
        var explicitCtor = constructors.FirstOrDefault(c =>
            c.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "Pragmatic.Mapping.Attributes.MapConstructorAttribute"));

        var selectedCtor = explicitCtor ?? SelectBestConstructor(constructors, dtoType);

        if (selectedCtor is null)
            return (ImmutableArray<ConstructorParameterModel>.Empty, false);

        var parameters = selectedCtor.Parameters
            .Select(p => new ConstructorParameterModel
            {
                Name = p.Name,
                Type = p.Type.ToDisplayString(),
                IsNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated,
                MatchingPropertyName = FindMatchingDtoProperty(p.Name, dtoType),
                IsOptional = p.HasExplicitDefaultValue
            })
            .ToImmutableArray();

        return (parameters, explicitCtor is not null);
    }

    /// <summary>
    ///     Selects the best constructor based on parameter matching with DTO properties.
    /// </summary>
    public static IMethodSymbol? SelectBestConstructor(
        List<IMethodSymbol> constructors,
        INamedTypeSymbol dtoType)
    {
        var dtoProperties = ToHashSetCompat(
            PropertyAnalyzer.GetAllProperties(dtoType).Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        // Score each constructor by how many parameters match DTO properties
        return constructors
            .Select(c => (ctor: c, score: c.Parameters.Count(p =>
                dtoProperties.Contains(p.Name))))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.ctor.Parameters.Length) // Prefer simpler constructor if same score
            .Select(x => x.ctor)
            .FirstOrDefault();
    }

    private static HashSet<string> ToHashSetCompat(IEnumerable<string> source, StringComparer comparer)
    {
        var set = new HashSet<string>(comparer);
        foreach (var item in source)
            set.Add(item);
        return set;
    }

    private static string? FindMatchingDtoProperty(string paramName, INamedTypeSymbol dtoType)
    {
        return PropertyAnalyzer.GetAllProperties(dtoType)
            .FirstOrDefault(p => string.Equals(p.Name, paramName, StringComparison.OrdinalIgnoreCase))
            ?.Name;
    }
}
