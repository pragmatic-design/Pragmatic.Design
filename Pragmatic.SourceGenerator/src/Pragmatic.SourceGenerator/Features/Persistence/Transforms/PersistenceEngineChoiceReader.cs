using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Decides the persistence engine of every entity this compilation declares, and returns the choices that cannot
///     stand.
/// </summary>
/// <remarks>
///     <para>
///         An entity's engine is the one its boundary names with <c>[PersistenceEngine]</c>, the boundary found by
///         <see cref="BoundaryOwnershipReader.BoundaryTypeOf" />; without a marker, the only engine the compilation
///         references. An engine is referenced when its bridge is (<see cref="DetectedFeatures.HasPersistenceEFCore" />,
///         <see cref="DetectedFeatures.HasPersistenceSql" />, <see cref="DetectedFeatures.HasPersistenceDocuments" />).
///     </para>
///     <para>
///         Read from the compilation because the marker sits on the boundary and the boundary is not visible from a
///         per-entity transform. A compilation that references no engine reports nothing: a module that leaves the
///         engine to its host has nothing to check the marker against.
///     </para>
/// </remarks>
internal static class PersistenceEngineChoiceReader
{
    private const string EntityAttributeName = "Pragmatic.Persistence.Entity.EntityAttribute";
    private const string MarkerAttributeName = "Pragmatic.Persistence.Entity.PersistenceEngineAttribute";

    // The numbers of Pragmatic.Persistence.Entity.PersistenceEngineKind, which is what the attribute carries.
    private static readonly string[] EngineNames = ["EfCore", "Sql", "Documents"];

    public static EquatableArray<PersistenceEngineProblem> Read(Compilation compilation, DetectedFeatures features, CancellationToken ct)
    {
        var referenced = Referenced(features);
        if (referenced.Count == 0)
            return EquatableArray<PersistenceEngineProblem>.Empty;

        var entityAttribute = compilation.GetTypeByMetadataName(EntityAttributeName);
        var marker = compilation.GetTypeByMetadataName(MarkerAttributeName);
        if (entityAttribute is null || marker is null)
            return EquatableArray<PersistenceEngineProblem>.Empty;

        var referencedText = string.Join(", ", referenced.Select(e => EngineNames[e]));
        var problems = ImmutableArray.CreateBuilder<PersistenceEngineProblem>();
        var reportedBoundaries = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var entity in EnumerateTypes(compilation.Assembly.GlobalNamespace, ct))
        {
            if (!HasAttribute(entity, entityAttribute))
                continue;

            var boundary = BoundaryOwnershipReader.BoundaryTypeOf(entity);
            var named = boundary is null ? null : NamedEngine(boundary, marker);

            if (named is { } engine)
            {
                // Once per boundary, where the fix is: every entity of that boundary has the same problem.
                if (!referenced.Contains(engine) && reportedBoundaries.Add(boundary!))
                    problems.Add(new PersistenceEngineProblem(
                        PersistenceEngineProblemKind.EngineNotReferenced,
                        boundary!.ToDisplayString(),
                        boundary.ToDisplayString(),
                        EngineName(engine),
                        referencedText,
                        LocationInfo.From(boundary.Locations.FirstOrDefault())));
            }
            else if (referenced.Count >= 2)
            {
                problems.Add(new PersistenceEngineProblem(
                    PersistenceEngineProblemKind.NoEngineNamed,
                    entity.ToDisplayString(),
                    boundary?.ToDisplayString() ?? "(none)",
                    null,
                    referencedText,
                    LocationInfo.From(entity.Locations.FirstOrDefault())));
            }
        }

        return problems.ToImmutable();
    }

    private static List<int> Referenced(DetectedFeatures features)
    {
        var referenced = new List<int>(3);
        if (features.HasPersistenceEFCore) referenced.Add(0);
        if (features.HasPersistenceSql) referenced.Add(1);
        if (features.HasPersistenceDocuments) referenced.Add(2);
        return referenced;
    }

    private static int? NamedEngine(INamedTypeSymbol boundary, INamedTypeSymbol marker)
    {
        foreach (var attribute in boundary.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker)
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is int value)
                return value;
        }

        return null;
    }

    private static string EngineName(int engine)
        => engine >= 0 && engine < EngineNames.Length ? EngineNames[engine] : engine.ToString();

    private static bool HasAttribute(INamedTypeSymbol type, INamedTypeSymbol attribute)
    {
        foreach (var candidate in type.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute))
                return true;
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns, CancellationToken ct)
    {
        foreach (var member in ns.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            switch (member)
            {
                case INamespaceSymbol nested:
                    foreach (var type in EnumerateTypes(nested, ct))
                        yield return type;
                    break;
                case INamedTypeSymbol type:
                    yield return type;
                    break;
            }
        }
    }
}
