using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Caching.Models;

namespace Pragmatic.SourceGenerator.Features.Caching.Transforms;

internal static class CachingTransform
{
    // =========================================================================
    // Transform [Cacheable]
    // =========================================================================

    public static CacheableModel? TransformCacheable(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        var attribute = context.Attributes[0];

        var duration = attribute.GetNamedArgument<string>("Duration") ?? "5m";
        var sliding = attribute.GetNamedArgument<bool>("Sliding");
        var priority = attribute.GetNamedArgument<int>("Priority");

        // Category type from [Cacheable(Category = typeof(...))]
        string? categoryTypeFqn = null;
        var categoryArg = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Category");
        if (categoryArg.Value.Value is INamedTypeSymbol categoryType)
            categoryTypeFqn = categoryType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var tagsArg = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Tags");
        var tags = ImmutableArray<string>.Empty;
        if (tagsArg.Value is { IsNull: false, Values.Length: > 0 })
            tags = tagsArg.Value.Values
                .Where(v => v.Value is string)
                .Select(v => (string)v.Value!)
                .ToImmutableArray();

        var allPublicProperties = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public &&
                        p is { IsStatic: false, GetMethod: not null })
            .ToList();

        var unreadable = new List<string>();
        var keyProperties = GetKeyProperties(allPublicProperties, unreadable).ToImmutableArray();

        return new CacheableModel
        {
            Namespace = symbol.GetNamespaceOrEmpty(),
            TypeName = symbol.Name,
            Accessibility = symbol.GetAccessibilityKeyword(),
            TypeKind = symbol.GetTypeKindKeyword(),
            Duration = duration,
            Sliding = sliding,
            Priority = priority,
            Tags = tags,
            KeyProperties = keyProperties,
            UnreadableKeyProperties = unreadable.ToImmutableArray(),
            IsPartial = symbol.IsPartial(),
            TotalPropertyCount = allPublicProperties.Count,
            AllPropertyNames = allPublicProperties.Select(p => p.Name).ToImmutableArray(),
            CategoryTypeFqn = categoryTypeFqn,
            Location = LocationInfo.From(symbol.Locations.FirstOrDefault())
        };
    }

    /// <summary>
    ///     The properties that make up the key, in the order they appear in it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The implicit numbering starts <b>after</b> the highest explicit <c>Order</c>, which is
    ///     what <c>CacheKeyAttribute</c> documents: "properties that do not specify an explicit Order
    ///     are sorted after all explicitly-ordered properties". Starting at 0 and sharing the number
    ///     space, a single <c>[CacheKey(Order = 0)]</c> — the attribute's own example — would collide
    ///     with whatever property happened to be declared first, and <c>PRAG1750</c> would report the
    ///     collision the transform had just created. A test that orders <em>every</em> property cannot
    ///     see it: that is the one arrangement in which the two rules agree.
    /// </remarks>
    private static IEnumerable<CacheKeyPropertyModel> GetKeyProperties(
        List<IPropertySymbol> properties,
        List<string> unreadable)
    {
        var order = HighestExplicitOrder(properties) + 1;

        foreach (var prop in properties)
        {
            var cacheKeyAttr = prop.GetAttribute("Pragmatic.Caching.Attributes.CacheKeyAttribute");

            if (cacheKeyAttr?.GetNamedArgument<bool>("Exclude") == true)
                continue;

            var keyName = cacheKeyAttr?.GetNamedArgument<string>("Name") ?? prop.Name;
            // ⚠️ GetWrittenIntOrDefault and not `?.GetNamedArgument<int>(…) ?? int.MaxValue`, which
            // reads the same and is not: the generic helper yields 0 for an argument nobody wrote,
            // and 0 is not null — so [CacheKey(Name = "city")] silently ordered that property 0,
            // every property carrying a bare [CacheKey] collided at 0, and PRAG1750 reported the
            // collision the transform had invented. The helper exists for this exact trap.
            var explicitOrder = cacheKeyAttr.GetWrittenIntOrDefault("Order", int.MaxValue);
            var parts = BuildParts(prop.Type, keyName, out var truncated);
            if (truncated)
                unreadable.Add(prop.Name);

            yield return new CacheKeyPropertyModel
            {
                Name = prop.Name,
                KeyName = keyName,
                Type = prop.Type.ToRenderName(),
                Order = explicitOrder == int.MaxValue ? order++ : explicitOrder,
                IsCollection = IsCollectionType(prop.Type),
                Parts = parts.ToImmutableArray()
            };
        }
    }

    /// <summary>
    ///     The largest <c>Order</c> anybody wrote, or <c>-1</c> when nobody did — in which case the
    ///     implicit numbering starts at 0 and nothing changes for a type that orders nothing.
    /// </summary>
    private static int HighestExplicitOrder(List<IPropertySymbol> properties)
    {
        var highest = -1;

        foreach (var prop in properties)
        {
            var declared = prop
                .GetAttribute("Pragmatic.Caching.Attributes.CacheKeyAttribute")
                .GetWrittenIntOrDefault("Order", int.MaxValue);

            if (declared != int.MaxValue && declared > highest)
                highest = declared;
        }

        return highest;
    }

    /// <summary>How deep the walk into a complex property goes before it gives up.</summary>
    /// <remarks>
    ///     Three levels covers the filters this exists for — <c>location.cityGroup.city</c> is two —
    ///     and bounds a key that would otherwise grow with the shape of a graph. Beyond it the property
    ///     is reported (PRAG1705) rather than silently truncated: a truncated key under-differentiates,
    ///     which is the defect this walk removes.
    /// </remarks>
    private const int MaxKeyDepth = 3;

    /// <summary>
    ///     The fragments a property contributes: itself when it is a scalar or a collection, one per
    ///     nested scalar when it is a complex object.
    /// </summary>
    /// <param name="type">The property's type.</param>
    /// <param name="label">What the property is called in the key — its <c>[CacheKey(Name)]</c> or its own.</param>
    /// <param name="truncated">
    ///     Set when the walk gave up — too deep, or a type that refers to itself. The fragments found
    ///     so far still differentiate; the ones below the limit do not, so the caller reports PRAG1705
    ///     rather than shipping a key that is silently blind past that point.
    /// </param>
    private static IEnumerable<CacheKeyPartModel> BuildParts(ITypeSymbol type, string label, out bool truncated)
    {
        truncated = false;
        if (IsScalarForKey(type) || IsCollectionType(type))
            return [new CacheKeyPartModel(label, string.Empty, IsCollectionType(type))];

        var parts = new List<CacheKeyPartModel>();
        Walk(type, label, string.Empty, 1, [], parts, ref truncated);
        return parts;
    }

    private static void Walk(
        ITypeSymbol type,
        string label,
        string tail,
        int depth,
        List<ITypeSymbol> seen,
        List<CacheKeyPartModel> parts,
        ref bool truncated)
    {
        if (depth > MaxKeyDepth)
        {
            truncated = true;
            return;
        }

        // A filter that references its own type would otherwise walk forever.
        foreach (var visited in seen)
        {
            if (SymbolEqualityComparer.Default.Equals(visited, type))
            {
                truncated = true;
                return;
            }
        }

        seen.Add(type);

        foreach (var prop in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (prop.DeclaredAccessibility != Accessibility.Public || prop.IsStatic || prop.GetMethod is null)
                continue;
            if (prop.GetAttribute("Pragmatic.Caching.Attributes.CacheKeyAttribute")
                    ?.GetNamedArgument<bool>("Exclude") == true)
                continue;

            var nestedLabel = $"{label}.{prop.Name}";
            var nestedTail = $"{tail}?.{prop.Name}";

            if (IsScalarForKey(prop.Type) || IsCollectionType(prop.Type))
                parts.Add(new CacheKeyPartModel(nestedLabel, nestedTail, IsCollectionType(prop.Type)));
            else
                Walk(prop.Type, nestedLabel, nestedTail, depth + 1, seen, parts, ref truncated);
        }

        seen.Remove(type);
    }

    /// <summary>
    ///     Whether a type is something <c>Convert.ToString</c> renders as its <em>value</em>.
    /// </summary>
    /// <remarks>
    ///     The question the old code never asked. Primitives, strings, enums and the BCL structs that
    ///     have a meaningful <c>ToString</c> qualify; a class does not, and neither does a struct of the
    ///     user's own — both render as the type name, which is the same for every instance.
    /// </remarks>
    private static bool IsScalarForKey(ITypeSymbol type)
    {
        var underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

        if (underlying.TypeKind == TypeKind.Enum)
            return true;

        if (underlying.SpecialType is not SpecialType.None)
            return true;

        return underlying.ToDisplayString() is
            "System.Guid" or "System.DateTime" or "System.DateTimeOffset" or "System.TimeSpan"
            or "System.DateOnly" or "System.TimeOnly" or "System.Uri" or "System.Version";
    }

    /// <summary>
    ///     A member is a "collection" for cache-key purposes when it is an array or implements
    ///     <see cref="System.Collections.IEnumerable"/> — but NOT a string (which is enumerable yet
    ///     serializes to its own value correctly).
    /// </summary>
    private static bool IsCollectionType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return false;
        // Only reference-type collections (List<T>, T[], IReadOnlyList<T>, HashSet<T>, …): the generated
        // key uses a `== null` guard, which does not compile for struct collections (e.g. ImmutableArray<T>).
        // Those remain on the scalar path — a rare edge that at worst under-differentiates, never breaks build.
        if (!type.IsReferenceType)
            return false;
        if (type is IArrayTypeSymbol)
            return true;
        if (type.SpecialType == SpecialType.System_Collections_IEnumerable)
            return true;
        return type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable);
    }

    // =========================================================================
    // Transform [InvalidatesCache]
    // =========================================================================

    public static InvalidatesModel? TransformInvalidates(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        var attribute = context.Attributes[0];

        var tags = ImmutableArray<string>.Empty;
        if (attribute.ConstructorArguments.Length > 0 &&
            attribute.ConstructorArguments[0].Kind == TypedConstantKind.Array)
            tags = attribute.ConstructorArguments[0].Values
                .Where(v => v.Value is string)
                .Select(v => (string)v.Value!)
                .ToImmutableArray();

        // Category type from [InvalidatesCache(Category = typeof(...))]
        string? invalidateCategoryFqn = null;
        var invalidateCategoryArg = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Category");
        if (invalidateCategoryArg.Value.Value is INamedTypeSymbol invalidateCategoryType)
            invalidateCategoryFqn = invalidateCategoryType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var keys = ImmutableArray<string>.Empty;
        var keysArg = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Keys");
        if (keysArg.Value is { IsNull: false, Values.Length: > 0 })
            keys = keysArg.Value.Values
                .Where(v => v.Value is string)
                .Select(v => (string)v.Value!)
                .ToImmutableArray();

        // Convention: if no tags, derive from type name
        if (tags.IsDefaultOrEmpty)
            tags = ImmutableArray.Create(GetConventionTag(symbol.Name));

        var properties = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public &&
                        p is { IsStatic: false, GetMethod: not null })
            .Select(p => new PlaceholderPropertyModel { Name = p.Name, Type = p.Type.ToRenderName() })
            .ToImmutableArray();

        return new InvalidatesModel
        {
            Namespace = symbol.GetNamespaceOrEmpty(),
            TypeName = symbol.Name,
            Accessibility = symbol.GetAccessibilityKeyword(),
            TypeKind = symbol.GetTypeKindKeyword(),
            Tags = tags,
            Keys = keys,
            Properties = properties,
            IsPartial = symbol.IsPartial(),
            TypeFqn = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsDomainEvent = ImplementsDomainEvent(symbol),
            CategoryTypeFqn = invalidateCategoryFqn,
            Location = LocationInfo.From(symbol.Locations.FirstOrDefault())
        };
    }

    /// <summary>
    ///     The attribute documents itself as marking a <i>domain event</i>. Whether it actually is one
    ///     decides which invalidation path is generated, so it is read from the type, not assumed.
    /// </summary>
    private static bool ImplementsDomainEvent(INamedTypeSymbol symbol)
    {
        foreach (var iface in symbol.AllInterfaces)
            if (iface.ToDisplayString() == DomainEventInterfaceName)
                return true;

        return false;
    }

    private const string DomainEventInterfaceName = "Pragmatic.Events.IDomainEvent";

    private static string GetConventionTag(string typeName)
    {
        foreach (var suffix in new[] { "Event", "Updated", "Created", "Deleted", "Changed" })
            if (typeName.EndsWith(suffix) && typeName.Length > suffix.Length)
            {
                typeName = typeName.Substring(0, typeName.Length - suffix.Length);
                break;
            }

        return SimplePluralize(typeName).ToLowerInvariant();
    }

    private static string SimplePluralize(string word)
    {
        if (string.IsNullOrEmpty(word))
            return word;

        // Known irregular plurals
        var lowerWord = word.ToLowerInvariant();
        switch (lowerWord)
        {
            case "person": return ReplaceKeepingCase(word, "People");
            case "child": return word + "ren";
            case "data": return word;
            case "status": return word + "es";
            case "index": return word.Substring(0, word.Length - 2) + "ices";
            case "matrix": return word.Substring(0, word.Length - 2) + "ices";
        }

        // Words ending in 's', 'x', 'z', 'ch', 'sh' -> add 'es'
        if (word.EndsWith("s") || word.EndsWith("S") ||
            word.EndsWith("x") || word.EndsWith("X") ||
            word.EndsWith("z") || word.EndsWith("Z") ||
            word.EndsWith("ch") || word.EndsWith("Ch") || word.EndsWith("CH") ||
            word.EndsWith("sh") || word.EndsWith("Sh") || word.EndsWith("SH"))
            return word + "es";

        // Words ending in 'y' preceded by a consonant -> change 'y' to 'ies'
        if ((word.EndsWith("y") || word.EndsWith("Y")) &&
            word.Length > 1 &&
            !IsVowel(word[word.Length - 2]))
            return word.Substring(0, word.Length - 1) + "ies";

        return word + "s";
    }

    private static bool IsVowel(char c)
        => "aeiouAEIOU".IndexOf(c) >= 0;

    private static string ReplaceKeepingCase(string original, string replacement)
        => char.IsUpper(original[0])
            ? char.ToUpperInvariant(replacement[0]) + replacement.Substring(1)
            : char.ToLowerInvariant(replacement[0]) + replacement.Substring(1);
}
