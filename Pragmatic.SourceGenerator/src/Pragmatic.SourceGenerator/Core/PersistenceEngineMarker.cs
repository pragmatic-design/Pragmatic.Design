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

    /// <summary>The name of the EF Core engine, as <see cref="EngineOf(string, DetectedFeatures)" /> answers it.</summary>
    public const string EfCore = "EfCore";

    /// <summary>The name of the Sql engine, as <see cref="EngineOf(string, DetectedFeatures)" /> answers it.</summary>
    public const string Sql = "Sql";

    /// <summary>The name of the documents engine, as <see cref="EngineOf(string, DetectedFeatures)" /> answers it.</summary>
    public const string Documents = "Documents";

    /// <summary>
    ///     The engine that persists <paramref name="entity" /> in this compilation: the one its boundary names, else
    ///     the only engine the compilation references. Null when that is undecided (two referenced, none named:
    ///     <c>PRAG0743</c>) or there is no engine at all.
    /// </summary>
    /// <remarks>
    ///     Decided here, against this compilation's references, and nowhere else: the module's metadata carries only
    ///     what the boundary named, since a module and its host may reference different engines.
    /// </remarks>
    public static string? EngineOf(Features.Persistence.Models.EntityMetadataModel entity, DetectedFeatures features)
        => EngineOf(entity.NamedPersistenceEngine, features);

    /// <summary>The same rule from the engine the entity's boundary names (<see cref="NamedFor" />): what a model that is not an entity's carries.</summary>
    public static string? EngineOf(string? named, DetectedFeatures features)
    {
        if (named is not null)
            return named;

        var referenced = Referenced(features);
        return referenced.Length == 1 ? referenced[0] : null;
    }

    /// <summary>Whether EF Core persists <paramref name="entity" />: what every EF-only output asks of it.</summary>
    public static bool IsEfCore(Features.Persistence.Models.EntityMetadataModel entity, DetectedFeatures features)
        => EngineOf(entity, features) == EfCore;

    /// <summary>
    ///     Whether an engine the compilation references persists <paramref name="entity" />: what the entity's shape
    ///     asks, since every engine reads the same members.
    /// </summary>
    public static bool HasEngine(Features.Persistence.Models.EntityMetadataModel entity, DetectedFeatures features)
        => EngineOf(entity, features) is { } engine && Referenced(features).Contains(engine);

    /// <summary>Whether the compilation references any engine's bridge.</summary>
    public static bool AnyReferenced(DetectedFeatures features)
        => features.HasPersistenceEFCore || features.HasPersistenceSql || features.HasPersistenceDocuments;

    /// <summary>The engines whose bridge the compilation references, in the order of the enum.</summary>
    public static ImmutableArray<string> Referenced(DetectedFeatures features)
    {
        var referenced = ImmutableArray.CreateBuilder<string>(3);
        if (features.HasPersistenceEFCore) referenced.Add(EfCore);
        if (features.HasPersistenceSql) referenced.Add(Names[1]);
        if (features.HasPersistenceDocuments) referenced.Add(Names[2]);
        return referenced.ToImmutable();
    }
}
