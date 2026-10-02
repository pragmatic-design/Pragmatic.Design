using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Mapping.Analysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transforms [LoadWith&lt;T&gt;] on DTO/query classes into <see cref="LoadingProfileModel"/>.
///     Analyzes DTO properties that match entity navigations to determine Include paths.
/// </summary>
internal static class LoadingProfileTransform
{
    public const string LoadWithAttributeName = "Pragmatic.Persistence.Query.LoadWithAttribute`1";

    public static LoadingProfileModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var attr = context.Attributes.FirstOrDefault();
        if (attr?.AttributeClass is not { IsGenericType: true })
            return null;

        // Extract entity type from LoadWith<T>
        var entityType = attr.AttributeClass.TypeArguments[0];

        // Read attribute parameters
        var maxDepth = 1;
        var splitQuery = false;

        foreach (var named in attr.NamedArguments)
        {
            switch (named.Key)
            {
                case "MaxDepth" when named.Value.Value is int d:
                    maxDepth = d;
                    break;
                case "SplitQuery" when named.Value.Value is bool sq:
                    splitQuery = sq;
                    break;
            }
        }

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : typeSymbol.ContainingNamespace.ToDisplayString();

        // Include ALL entity navigations up to MaxDepth (not just DTO-matching ones).
        // [LoadWith<T>(MaxDepth=N)] means "load all navigations of T up to depth N".
        var entityNavPaths = GetAllEntityNavigations(entityType, maxDepth);

        // Find DTO properties that look like navigations but don't match entity navs
        var unmatchedNavs = FindUnmatchedNavigations(typeSymbol, entityType, entityNavPaths);

        // Auto-detect SplitQuery if not explicitly set: 2+ collection navigations → split
        if (!splitQuery)
        {
            var collectionCount = CountCollectionNavigations(entityType, entityNavPaths);
            splitQuery = collectionCount >= 2;
        }

        return new LoadingProfileModel
        {
            Namespace = ns,
            TypeName = typeSymbol.Name,
            FullTypeName = $"global::{typeSymbol.ToDisplayString()}",
            EntityTypeName = entityType.Name,
            EntityFullTypeName = $"global::{entityType.ToDisplayString()}",
            MaxDepth = maxDepth,
            SplitQuery = splitQuery,
            NavigationPaths = entityNavPaths,
            UnmatchedNavigationNames = unmatchedNavs
        };
    }

    /// <summary>
    ///     Gets ALL navigation property paths from the entity type up to the specified depth.
    ///     [LoadWith] includes all navigations, not just those matching DTO properties.
    /// </summary>
    internal static ImmutableArray<string> GetAllEntityNavigations(
        ITypeSymbol entityType,
        int maxDepth)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        CollectNavigationPaths(entityType, maxDepth, "", builder, new HashSet<string>());
        return builder.ToImmutable();
    }

    /// <summary>
    ///     Collects the include paths of <paramref name="type" />, down to <paramref name="remainingDepth" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>cameFrom</c> is the type the current path arrived from, null at the root. A navigation
    ///         pointing back at it is skipped: EF Core refuses an include that walks back up the include
    ///         tree — "the navigation was ignored from 'Include' since the fix-up will automatically
    ///         populate it" — raised as an error by any application that promotes that warning, and
    ///         pointless either way, because the fix-up does populate it.
    ///     </para>
    ///     <para>
    ///         Only the immediate parent, not every ancestor: a self-relation at the root —
    ///         <c>Include(e =&gt; e.Parent)</c> on a tree node — is a legitimate first hop, and the
    ///         prohibition is on going back the way you came.
    ///     </para>
    /// </remarks>
    private static void CollectNavigationPaths(
        ITypeSymbol type,
        int remainingDepth,
        string prefix,
        ImmutableArray<string>.Builder builder,
        HashSet<string> visited,
        ITypeSymbol? cameFrom = null)
    {
        if (remainingDepth <= 0)
            return;

        var typeFqn = type.ToDisplayString();
        if (!visited.Add(typeFqn))
            return; // Prevent cycles

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;
            if (!IsEntityNavigation(prop))
                continue;

            var navType = UnwrapCollectionType(prop.Type) ?? prop.Type;
            if (PointsBackAt(navType, cameFrom))
                continue;

            seen.Add(prop.Name);

            var path = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
            builder.Add(path);

            // For depth > 1, recurse into navigation type
            if (remainingDepth > 1)
                CollectNavigationPaths(navType, remainingDepth - 1, path, builder, visited, type);
        }

        // The navigations that exist only because [Relation.*] describes them. They are emitted by this
        // same generator pass, so the symbol above does not carry them yet — a generator cannot see
        // another generator's output. Reading them here is what makes MaxDepth mean what it says:
        // before this, a profile over an entity that declares its relations with attributes silently
        // included only whichever navigations the author had also written by hand.
        foreach (var relation in CollectRelationNavigations(type))
        {
            if (PointsBackAt(relation.TargetType, cameFrom))
                continue;

            if (!seen.Add(relation.NavigationName))
                continue;

            var path = string.IsNullOrEmpty(prefix)
                ? relation.NavigationName
                : $"{prefix}.{relation.NavigationName}";
            builder.Add(path);

            if (remainingDepth > 1)
                CollectNavigationPaths(relation.TargetType, remainingDepth - 1, path, builder, visited, type);
        }

        visited.Remove(typeFqn); // Allow revisiting from different paths
    }

    /// <summary>Whether this navigation goes back to the type the path arrived from.</summary>
    private static bool PointsBackAt(ITypeSymbol navigationTarget, ITypeSymbol? cameFrom)
        => cameFrom is not null
           && SymbolEqualityComparer.Default.Equals(navigationTarget.OriginalDefinition, cameFrom.OriginalDefinition);

    /// <summary>
    ///     Navigations the entity declares through <c>[Relation.*]</c> and that the generator will
    ///     therefore emit — minus the ones it will not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A relation whose target lives in another boundary produces a foreign key and <b>no</b>
    ///         navigation: the two entities land in different DbContexts and there is nothing for EF to
    ///         include. Including it here would generate <c>Include(e =&gt; e.Something)</c> against a
    ///         property that does not exist.
    ///     </para>
    ///     <para>
    ///         That rule is <c>RelationGraphBuilder.IsCrossBoundary</c>; this is its second reading, and
    ///         the two have to agree. Kept deliberately identical — empty on either side means "not
    ///         cross", otherwise a plain comparison — so a change there is findable from here.
    ///     </para>
    /// </remarks>
    private static IEnumerable<(string NavigationName, ITypeSymbol TargetType)> CollectRelationNavigations(
        ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
            yield break;

        var ownerBoundary = RelationTransform.FindBelongsToAttribute(named);

        foreach (var attribute in named.GetAttributes())
        {
            var info = RelationDetection.ClassifyRelationAttribute(attribute);
            if (info is null)
                continue;

            var targetBoundary = RelationTransform.FindBelongsToAttribute(info.TargetType);
            if (!string.IsNullOrEmpty(ownerBoundary)
                && !string.IsNullOrEmpty(targetBoundary)
                && ownerBoundary != targetBoundary)
                continue;

            yield return (RelationDetection.GetNavigationName(info), info.TargetType);
        }
    }

    private static ITypeSymbol? UnwrapCollectionType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsGenericType: true } named &&
            named.TypeArguments.Length == 1)
        {
            var name = named.OriginalDefinition.ToDisplayString();
            if (name.StartsWith("System.Collections.Generic.") ||
                name.StartsWith("System.Collections.Immutable."))
                return named.TypeArguments[0];
        }
        return null;
    }

    /// <summary>
    ///     Gets property names that look like navigations (reference types, collections, not primitives).
    /// </summary>
    private static HashSet<string> GetNavigationPropertyNames(ITypeSymbol entityType)
    {
        var navNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in entityType.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;
            if (IsEntityNavigation(prop))
                navNames.Add(prop.Name);
        }

        return navNames;
    }

    /// <summary>
    ///     Whether this entity property is a navigation the profile can <c>Include</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The entity-side rule, borrowed from <c>EntityTransform</c> rather than restated: what the
    ///         profile includes has to be exactly what the EF configuration mapped as a navigation, and
    ///         two rules for one question drift.
    ///     </para>
    ///     <para>
    ///         The obvious local rule — "any generic collection, or any reference type that is not a
    ///         string" — is wrong in both halves on a real entity: a <c>List&lt;string&gt;</c> is a JSON
    ///         primitive collection, and a <c>[ValueObject]</c> is flattened into the owner's columns.
    ///         <c>Include</c> over either is rejected by EF Core before a row is read, and no
    ///         <c>MaxDepth</c> avoids them.
    ///     </para>
    /// </remarks>
    private static bool IsEntityNavigation(IPropertySymbol prop)
        => !EntityTransform.IsPrimitiveCollectionProperty(prop) && EntityTransform.IsNavigationProperty(prop);

    /// <summary>
    ///     Whether this <b>DTO</b> property looks like a navigation — the question
    ///     <see cref="FindUnmatchedNavigations" /> asks, which is not the entity's question.
    /// </summary>
    /// <remarks>
    ///     A DTO carries no foreign keys, so the entity rule would answer "no" to every reference it
    ///     declares and <c>PRAG0710</c> would stop reporting the shape it exists for. What it must not
    ///     do is call a primitive collection or a value object a navigation: those are columns on both
    ///     sides, and reporting them was a false alarm about an include nobody was missing.
    /// </remarks>
    private static bool LooksLikeADtoNavigation(IPropertySymbol prop)
    {
        if (EntityTransform.IsPrimitiveCollectionProperty(prop))
            return false;

        var type = prop.Type;

        // Collections (ICollection<T>, IList<T>, List<T>, etc.)
        if (type is INamedTypeSymbol { IsGenericType: true } named)
        {
            var name = named.OriginalDefinition.ToDisplayString();
            if (name.StartsWith("System.Collections.Generic.", StringComparison.Ordinal) ||
                name.StartsWith("System.Collections.Immutable.", StringComparison.Ordinal))
                return true;
        }

        if (SqlTranslatableAnalyzer.IsValueObject(type))
            return false;

        // Reference types that are not string, not System types
        if (type is { TypeKind: TypeKind.Class, SpecialType: SpecialType.None } &&
            type.ToDisplayString() != "string")
            return true;

        return false;
    }

    /// <summary>
    ///     Finds DTO properties that look like navigations but don't match any entity navigation.
    /// </summary>
    private static ImmutableArray<string> FindUnmatchedNavigations(
        INamedTypeSymbol dtoType,
        ITypeSymbol entityType,
        ImmutableArray<string> matchedPaths)
    {
        var entityNavs = GetNavigationPropertyNames(entityType);
        var matchedSet = new HashSet<string>(matchedPaths, StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var member in dtoType.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;
            if (matchedSet.Contains(prop.Name))
                continue;
            if (!LooksLikeADtoNavigation(prop))
                continue;

            // DTO property looks like a navigation but doesn't match any entity nav
            builder.Add(prop.Name);
        }

        return builder.ToImmutable();
    }

    private static int CountCollectionNavigations(ITypeSymbol entityType, ImmutableArray<string> navPaths)
    {
        var count = 0;
        foreach (var path in navPaths)
        {
            var rootName = path.Split('.')[0];
            foreach (var member in entityType.GetMembers())
            {
                if (member is IPropertySymbol prop && prop.Name == rootName)
                {
                    if (prop.Type is INamedTypeSymbol { IsGenericType: true } named)
                    {
                        var name = named.OriginalDefinition.ToDisplayString();
                        if (name.StartsWith("System.Collections.Generic."))
                            count++;
                    }
                    break;
                }
            }
        }
        return count;
    }
}
