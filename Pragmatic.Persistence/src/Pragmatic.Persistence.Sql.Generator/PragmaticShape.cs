using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.Persistence.Sql.Generator.Templates;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     Pragmatic's <c>[Entity]</c> under the Sql engine: the members Pragmatic's own generator declares on the partial class (the key, the
///     stamps, the owner, the scopes, the navigations and their foreign keys), declared here when that generator does not. It declares them
///     only when EF Core is referenced (its gate is <c>Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute</c>), and no generator sees
///     another's output: without this, a chain reading <c>r.Property</c> binds to nothing. The text mirrors Pragmatic's templates member for
///     member (<c>EntityTraitsTemplate</c>, <c>OwnershipPropertyTemplate</c>, <c>ScopedPropertyTemplate</c>, <c>EntityRelationsTemplate</c>);
///     <c>ShapeParityTests</c> compares it with what they emit. Ported, this becomes the split of Pragmatic's gate.
/// </summary>
internal static class PragmaticShape
{
    private const string EntityAttribute = "Pragmatic.Persistence.Entity.EntityAttribute";
    private const string EfGate = "Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute";

    /// <summary>True when Pragmatic's generator declares the shape (EF Core referenced): members this generator cannot see.</summary>
    public static bool DeclaredByPragmatic(Compilation compilation)
        => compilation.GetTypeByMetadataName(EfGate) is not null && compilation.GetTypeByMetadataName(EntityAttribute) is not null;

    /// <summary>True for a Pragmatic <c>[Entity]</c>.</summary>
    public static bool IsPragmaticEntity(ITypeSymbol? type)
        => type is INamedTypeSymbol named && named.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == EntityAttribute);

    /// <summary>
    ///     The shape's source per entity (hint name, text); empty when EF Core's gate is there (Pragmatic declares it) or there is no Pragmatic
    ///     entity. Only the entities <paramref name="engine" /> serves (<see cref="PragmaticEngines" />). <paramref name="sqlMapping" />: the Sql
    ///     engine's mapping marks (a many-to-many's join table); the documents engine takes none.
    /// </summary>
    public static List<(string Hint, string Source)> For(Compilation compilation, PragmaticEngine engine, bool sqlMapping = true)
    {
        var shapes = new List<(string, string)>();
        if (compilation.GetTypeByMetadataName(EfGate) is not null || compilation.GetTypeByMetadataName(EntityAttribute) is not { } attribute)
            return shapes;
        var engines = PragmaticEngines.Of(compilation);
        var entities = PragmaticNames.AllTypes(compilation.GlobalNamespace)
            .Where(t => t.DeclaringSyntaxReferences.Length > 0 && t.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute)) && engines.Serves(t, engine))
            .ToList();
        var relations = PragmaticRelations.Read(entities, sqlMapping);
        foreach (var entity in entities)
        {
            var text = Text(entity, relations);
            if (text is not null)
                shapes.Add((HintOf(entity), text));
        }

        return shapes;
    }

    private static string HintOf(INamedTypeSymbol entity) => $"{entity.ToDisplayString().Replace('.', '_')}.PragmaticShape.g.cs";

    private static string? Text(INamedTypeSymbol entity, PragmaticRelations relations)
    {
        var declared = new HashSet<string>(entity.GetMembers().Select(m => m.Name));
        var interfaces = new List<string>();
        var members = new List<ShapeMemberModel>();
        void Member(ShapeMemberKind kind)
        {
            if (declared.Add(kind.ToString()))
                members.Add(new ShapeMemberModel(kind, kind.ToString()));
        }

        // [Entity]: the key Pragmatic assigns at construction, and Id as its alias (EntityTraitsTemplate). A derived type has its root's.
        if (!IsDerivedEntity(entity))
        {
            Member(ShapeMemberKind.PersistenceId);
            Member(ShapeMemberKind.Id);
        }

        // A trait whose members the source declares in full is the source's, interface included (EntityTransform.Attributes); a partial
        // declaration Pragmatic refuses (PRAG0624), so the members left are declared here.
        if (Has(entity, "SoftDeleteAttribute") && !AllDeclared(declared, "IsDeleted", "DeletedAt", "DeletedBy"))
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.ISoftDelete");
            Member(ShapeMemberKind.IsDeleted);
            Member(ShapeMemberKind.DeletedAt);
            Member(ShapeMemberKind.DeletedBy);
        }

        if (Has(entity, "AuditableAttribute") && !AllDeclared(declared, "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"))
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.IAuditable");
            Member(ShapeMemberKind.CreatedAt);
            Member(ShapeMemberKind.CreatedBy);
            Member(ShapeMemberKind.UpdatedAt);
            Member(ShapeMemberKind.UpdatedBy);
        }

        if (Has(entity, "HasOwnerAttribute") && declared.Add("OwnerId"))
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.IOwnershipAssignable");
            members.Add(new ShapeMemberModel(ShapeMemberKind.Owner, "OwnerId"));
        }

        if (Has(entity, "HasAccessScopesAttribute") && declared.Add("AccessScopes"))
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.IScopedEntity");
            members.Add(new ShapeMemberModel(ShapeMemberKind.AccessScopes, "AccessScopes"));
        }

        // [ConcurrencyAware]: the row version (owner's decision of 2026-10-22). Pragmatic's EF configuration keeps it as a shadow property, while
        // the attribute's documentation promises a uint RowVersion; here it is a ulong, wide enough for SQL Server's rowversion as for xmin.
        if (Has(entity, "ConcurrencyAwareAttribute"))
            Member(ShapeMemberKind.RowVersion);

        relations.AppendMembers(entity, declared, members);
        AppendChangeTracking(entity, declared, interfaces, members);
        var filters = new List<ShapeFilterModel>();
        PragmaticFilters.Append(entity, filters);
        if (members.Count == 0 && filters.Count == 0 && interfaces.Count == 0)
            return null;
        var model = new EntityShapeModel(
            HintOf(entity),
            entity.ContainingNamespace.IsGlobalNamespace ? null : entity.ContainingNamespace.ToDisplayString(),
            entity.Name,
            "global::" + entity.ToDisplayString(),
            interfaces.ToEquatableArray(),
            members.ToEquatableArray(),
            filters.ToEquatableArray());
        return new EntityShapeTemplate(model).ToSourceText().ToString();
    }

    /// <summary>
    ///     Pragmatic's change tracking (<c>EntitySettersTemplate</c>): on an entity whose source declares properties with a non-public setter,
    ///     <c>IChangeTracking</c> and one internal <c>Set{Prop}</c> per such property, recording the name when the value changes. Navigations
    ///     are not among them. A derived entity gets the setters only (<c>DerivedEntitySettersTemplate</c>): its root carries the tracking.
    /// </summary>
    private static void AppendChangeTracking(INamedTypeSymbol entity, HashSet<string> declared, List<string> interfaces, List<ShapeMemberModel> members)
    {
        var tracked = entity.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && !p.IsIndexer && p.SetMethod is { } set && set.DeclaredAccessibility != Accessibility.Public && !IsNavigation(p.Type))
            .ToList();
        if (tracked.Count == 0)
            return;
        if (!IsDerivedEntity(entity) && declared.Add("ModifiedProperties"))
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.IChangeTracking");
            members.Add(new ShapeMemberModel(ShapeMemberKind.ChangeTracking, "ModifiedProperties"));
        }

        foreach (var property in tracked)
        {
            if (!declared.Add("Set" + property.Name))
                continue;
            var type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));
            members.Add(new ShapeMemberModel(ShapeMemberKind.TrackedSetter, property.Name, type, Required: !IsDerivedEntity(entity))); // a derived entity's root records the change
        }
    }

    /// <summary>A Pragmatic entity, or a collection of them: a navigation, which change tracking leaves out.</summary>
    private static bool IsNavigation(ITypeSymbol type)
        => IsPragmaticEntity(type) || (type is INamedTypeSymbol { IsGenericType: true } named && type.SpecialType != SpecialType.System_String && named.TypeArguments.Length == 1 && IsPragmaticEntity(named.TypeArguments[0]));

    private static bool AllDeclared(HashSet<string> declared, params string[] names) => names.All(declared.Contains);

    internal static bool Has(INamedTypeSymbol type, string attribute) => type.GetAttributes().Any(a => a.AttributeClass?.Name == attribute && a.AttributeClass.ContainingNamespace.ToDisplayString() == "Pragmatic.Persistence.Entity");

    /// <summary>A type whose base is a Pragmatic [Entity] too: the root declares the key.</summary>
    private static bool IsDerivedEntity(INamedTypeSymbol type)
        => type.BaseType is { } baseType && baseType.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == EntityAttribute);
}
