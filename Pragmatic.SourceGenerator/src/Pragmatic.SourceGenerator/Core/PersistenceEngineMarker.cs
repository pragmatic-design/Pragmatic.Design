using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Reads <c>[PersistenceEngine]</c> off a boundary, and names the engines a compilation references.
/// </summary>
/// <remarks>
///     One reader for every producer of the entity model: the per-symbol transforms, the package adoption and the
///     engine check all ask the same question, and a second copy of the attribute's decoding is how two of them
///     would come to disagree about which engine an entity has.
/// </remarks>
internal static class PersistenceEngineMarker
{
    private const string AttributeName = "Pragmatic.Persistence.Entity.PersistenceEngineAttribute";

    // The members of Pragmatic.Persistence.Entity.PersistenceEngineKind, by number: the attribute carries the number.
    private static readonly string[] Names = ["EfCore", "Sql", "Documents"];

    /// <summary>The engine the entity's boundary names, or null when the boundary names none or there is no boundary.</summary>
    public static string? NamedFor(INamedTypeSymbol entity)
        => BoundaryOwnershipReader.BoundaryTypeOf(entity) is { } boundary ? NamedOn(boundary) : null;

    /// <summary>The engine <paramref name="boundary" /> names with <c>[PersistenceEngine]</c>, or null.</summary>
    /// <remarks>
    ///     Matched by name rather than by symbol: a per-symbol transform has no compilation to resolve the attribute
    ///     type from, and the boundary may sit in a referenced assembly.
    /// </remarks>
    public static string? NamedOn(INamedTypeSymbol boundary)
    {
        foreach (var attribute in boundary.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != AttributeName
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not int engine)
                continue;

            return engine >= 0 && engine < Names.Length ? Names[engine] : engine.ToString();
        }

        return null;
    }

    /// <summary>The engines whose bridge the compilation references, in the order of the enum.</summary>
    public static ImmutableArray<string> Referenced(DetectedFeatures features)
    {
        var referenced = ImmutableArray.CreateBuilder<string>(3);
        if (features.HasPersistenceEFCore) referenced.Add(Names[0]);
        if (features.HasPersistenceSql) referenced.Add(Names[1]);
        if (features.HasPersistenceDocuments) referenced.Add(Names[2]);
        return referenced.ToImmutable();
    }
}
