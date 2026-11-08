using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Transforms;

/// <summary>
///     Transforms [PragmaticUser]-annotated classes into <see cref="UserEntityModel"/>.
/// </summary>
internal static class UserEntityTransform
{
    private static readonly HashSet<string> WellKnownProfileProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "PreferredCulture",
        "TimeZone"
    };

    public static UserEntityModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        var attribute = context.Attributes[0];

        var matchClaim = attribute.GetNamedArgument<string>("MatchClaim") ?? "sub";

        // The key type, from [Entity] or a declared Id
        var keyType = ResolveKeyType(symbol);
        if (keyType is null)
            return null;

        var (matchProperty, matchPropertyIsNavigation) = MatchOf(symbol, attribute.GetNamedArgument<string>("MatchProperty"));

        // Collect [ProfileProperty] properties
        var profileProperties = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(HasProfilePropertyAttribute)
            .Select(TransformProfileProperty)
            .ToImmutableArray();

        return new UserEntityModel
        {
            Namespace = symbol.GetNamespaceOrEmpty(),
            TypeName = symbol.Name,
            Accessibility = symbol.GetAccessibilityKeyword(),
            TypeKind = symbol.GetTypeKindKeyword(),
            KeyTypeName = keyType,
            MatchClaim = matchClaim,
            MatchProperty = matchProperty,
            MatchPropertyIsNavigation = matchPropertyIsNavigation,
            ProfileProperties = profileProperties,
            Members = ReadableMembers(symbol)
        };
    }

    /// <summary>
    ///     The members <c>[FromCurrentUser(member)]</c> may name: every public instance property with a
    ///     getter, the entity's own before its base's.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The <c>Id</c> of an <c>[Entity]</c> is added by hand. It is written by
    ///     <c>EntityTraitsTemplate</c> into this same compilation, so <c>GetMembers()</c> cannot return it
    ///     while this runs — and it is the member a query binds most often. <c>[Entity]</c> is what will
    ///     make it exist, and its type is always <see cref="Guid" />.
    /// </remarks>
    /// <summary>
    ///     The member path a <c>[PragmaticUser]</c>'s lookup compares with the key, or null when <paramref name="symbol" /> carries no
    ///     <c>[PragmaticUser]</c>: what an engine's read repository matches on (#593), the same one the resolver's model holds.
    /// </summary>
    internal static string? MatchPropertyOf(INamedTypeSymbol symbol)
        => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeNames.PragmaticUser) is { } attribute
            ? MatchOf(symbol, attribute.GetNamedArgument<string>("MatchProperty")).Property
            : null;

    /// <summary>The declared match property, or the default: <c>ExternalIdentityKey</c> on the entity, or on its navigation to an <c>IdentityRecord</c>.</summary>
    private static (string Property, bool IsNavigation) MatchOf(INamedTypeSymbol symbol, string? declared)
    {
        if (!string.IsNullOrEmpty(declared))
            return (declared!, false);

        var direct = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Any(p => p.Name == "ExternalIdentityKey" && p is { IsStatic: false, Type.SpecialType: SpecialType.System_String });
        if (direct)
            return ("ExternalIdentityKey", false);

        // A navigation property whose type inherits from IdentityRecord
        return FindIdentityRecordNavigation(symbol) is { } identityNav
            ? ($"{identityNav}!.ExternalIdentityKey", true)
            : ("ExternalIdentityKey", false);
    }

    private static ImmutableArray<UserMemberModel> ReadableMembers(INamedTypeSymbol symbol)
    {
        var members = ImmutableArray.CreateBuilder<UserMemberModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var type = symbol; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property is { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public, GetMethod: not null }
                    && seen.Add(property.Name))
                    members.Add(new UserMemberModel(
                        property.Name, property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }
        }

        if (ResolveEntityAttributeKeyType(symbol) is not null && seen.Add("Id"))
            members.Add(new UserMemberModel("Id", "global::System.Guid"));

        return members.ToImmutable();
    }

    /// <summary>
    ///     The type of the user entity's key, or null when nothing in the source declares one.
    /// </summary>
    /// <remarks>
    ///     Returning null suppresses the whole <c>[PragmaticUser]</c> pipeline, so every shape that has a
    ///     key must be recognised here. A Pragmatic entity is one of them and would fall through a plain
    ///     property lookup: it declares no Id of its own, and the one it gets is
    ///     written by <c>EntityTraitsTemplate</c> into this same compilation — a member
    ///     <c>GetMembers()</c> cannot return while the generator that creates it is running. The marker
    ///     that will cause <c>Id</c> to exist, <c>[Entity]</c>, is visible, and it carries the
    ///     same type the generated property will have.
    /// </remarks>
    private static string? ResolveKeyType(INamedTypeSymbol symbol)
    {
        // [Entity] — the declaration of the key, before the property exists.
        var entityKeyType = ResolveEntityAttributeKeyType(symbol);
        if (entityKeyType is not null)
            return entityKeyType;

        // Fallback: look for an Id property and use its type
        var idProperty = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => p.Name == "Id" && !p.IsStatic);

        return idProperty?.Type.ToDisplayString();
    }

    /// <summary>
    ///     The key type of an <c>[Entity]</c> from <c>Pragmatic.Persistence.Entity</c> — always
    ///     <c>System.Guid</c> — or null when the type is not a Pragmatic entity.
    /// </summary>
    private static string? ResolveEntityAttributeKeyType(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            // ⚠️ [Entity] carries no key type — it is always Guid. Answering null here for a user
            // entity would silently switch off the per-user culture provider and everything else
            // keyed off this model.
            if (attribute.AttributeClass is { Name: "EntityAttribute" } attributeClass &&
                attributeClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Entity")
            {
                return "System.Guid";
            }
        }

        return null;
    }

    /// <summary>
    ///     Finds a navigation property on the entity whose type inherits from IdentityRecord.
    ///     Returns the property name (e.g., "Identity") or null if not found.
    /// </summary>
    private static string? FindIdentityRecordNavigation(INamedTypeSymbol symbol)
    {
        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic) continue;

            var propType = property.Type;
            // Unwrap nullable (LocalIdentity? → LocalIdentity)
            if (propType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
                propType = nullable.TypeArguments[0];

            // Walk the type hierarchy to check for IdentityRecord base
            var current = propType as INamedTypeSymbol;
            while (current?.BaseType is not null)
            {
                if (current.BaseType.Name == "IdentityRecord" &&
                    current.BaseType.ContainingNamespace?.ToDisplayString() == "Pragmatic.Identity")
                    return property.Name;

                current = current.BaseType;
            }
        }

        return null;
    }

    private static bool HasProfilePropertyAttribute(IPropertySymbol property)
    {
        return property.GetAttributes()
            .Any(a => a.AttributeClass is not null &&
                      a.AttributeClass.Name == "ProfilePropertyAttribute" &&
                      a.AttributeClass.ContainingNamespace.ToDisplayString() == "Pragmatic.Identity");
    }

    private static ProfilePropertyModel TransformProfileProperty(IPropertySymbol property)
    {
        var typeName = property.Type.ToDisplayString();
        var isNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated;

        return new ProfilePropertyModel
        {
            Name = property.Name,
            TypeName = typeName,
            IsNullable = isNullable,
            IsWellKnown = WellKnownProfileProperties.Contains(property.Name)
        };
    }
}
