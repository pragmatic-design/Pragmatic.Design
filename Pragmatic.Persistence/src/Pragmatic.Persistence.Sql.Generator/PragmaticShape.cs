using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

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
                shapes.Add(($"{entity.ToDisplayString().Replace('.', '_')}.PragmaticShape.g.cs", text));
        }

        return shapes;
    }

    private static string? Text(INamedTypeSymbol entity, PragmaticRelations relations)
    {
        var declared = new HashSet<string>(entity.GetMembers().Select(m => m.Name));
        var interfaces = new List<string>();
        var body = new StringBuilder();
        void Member(string name, string text)
        {
            if (declared.Add(name))
                body.AppendLine(text);
        }

        // [Entity]: the key Pragmatic assigns at construction, and Id as its alias (EntityTraitsTemplate). A derived type has its root's.
        if (!IsDerivedEntity(entity))
        {
            Member("PersistenceId", "    public global::System.Guid PersistenceId { get; set; } = global::System.Guid.CreateVersion7();");
            Member("Id", "    public global::System.Guid Id => PersistenceId;");
        }

        // A trait whose members the source declares in full is the source's, interface included (EntityTransform.Attributes); a partial
        // declaration Pragmatic refuses (PRAG0624), so the members left are declared here.
        if (Has(entity, "SoftDeleteAttribute") && !AllDeclared(declared, "IsDeleted", "DeletedAt", "DeletedBy"))
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.ISoftDelete");
            Member("IsDeleted", "    public bool IsDeleted { get; set; }");
            Member("DeletedAt", "    public global::System.DateTimeOffset? DeletedAt { get; set; }");
            Member("DeletedBy", "    public string? DeletedBy { get; set; }");
        }

        if (Has(entity, "AuditableAttribute") && !AllDeclared(declared, "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"))
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.IAuditable");
            Member("CreatedAt", "    public global::System.DateTimeOffset CreatedAt { get; set; }");
            Member("CreatedBy", "    public string? CreatedBy { get; set; }");
            Member("UpdatedAt", "    public global::System.DateTimeOffset? UpdatedAt { get; set; }");
            Member("UpdatedBy", "    public string? UpdatedBy { get; set; }");
        }

        if (Has(entity, "HasOwnerAttribute") && declared.Add("OwnerId"))
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.IOwnershipAssignable");
            body.AppendLine("    public string OwnerId { get; private set; } = string.Empty;");
            body.AppendLine("    internal void SetOwnerId(string ownerId) => OwnerId = ownerId;");
            body.AppendLine("    void global::Pragmatic.Persistence.Entity.IOwnershipAssignable.AssignOwner(string ownerId) => OwnerId = ownerId;");
        }

        if (Has(entity, "HasAccessScopesAttribute") && declared.Add("AccessScopes"))
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.IScopedEntity");
            body.AppendLine("    public global::System.Collections.Generic.List<string> AccessScopes { get; private set; } = [];");
            body.AppendLine("    internal void GrantScope(string scope) { if (!AccessScopes.Contains(scope)) AccessScopes.Add(scope); }");
            body.AppendLine("    internal void RevokeScope(string scope) => AccessScopes.Remove(scope);");
        }

        // [ConcurrencyAware]: the row version (owner's decision of 2026-10-22). Pragmatic's EF configuration keeps it as a shadow property, while
        // the attribute's documentation promises a uint RowVersion; here it is a ulong, wide enough for SQL Server's rowversion as for xmin.
        if (Has(entity, "ConcurrencyAwareAttribute") && declared.Add("RowVersion"))
            body.AppendLine("    public ulong RowVersion { get; internal set; }");

        relations.AppendMembers(entity, declared, body);
        AppendChangeTracking(entity, declared, interfaces, body);
        PragmaticFilters.Append(entity, body);
        if (body.Length == 0 && interfaces.Count == 0)
            return null;

        var text = new StringBuilder();
        text.AppendLine("// <auto-generated/> Pragmatic.Persistence.Sql: the members Pragmatic's generator declares on this entity when EF Core is referenced (PragmaticShape).");
        text.AppendLine("#nullable enable");
        if (!entity.ContainingNamespace.IsGlobalNamespace)
            text.AppendLine($"namespace {entity.ContainingNamespace.ToDisplayString()};");
        text.AppendLine();
        text.AppendLine($"partial class {entity.Name}{(interfaces.Count == 0 ? "" : " : " + string.Join(", ", interfaces))}");
        text.AppendLine("{");
        text.Append(body);
        text.AppendLine("}");
        return text.ToString();
    }

    /// <summary>
    ///     Pragmatic's change tracking (<c>EntitySettersTemplate</c>): on an entity whose source declares properties with a non-public setter,
    ///     <c>IChangeTracking</c> and one internal <c>Set{Prop}</c> per such property, recording the name when the value changes. Navigations
    ///     are not among them. A derived entity gets the setters only (<c>DerivedEntitySettersTemplate</c>): its root carries the tracking.
    /// </summary>
    private static void AppendChangeTracking(INamedTypeSymbol entity, HashSet<string> declared, List<string> interfaces, StringBuilder body)
    {
        var tracked = entity.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && !p.IsIndexer && p.SetMethod is { } set && set.DeclaredAccessibility != Accessibility.Public && !IsNavigation(p.Type))
            .ToList();
        if (tracked.Count == 0)
            return;
        if (!IsDerivedEntity(entity) && declared.Add("ModifiedProperties"))
        {
            interfaces.Add("global::Pragmatic.Persistence.Entity.IChangeTracking");
            body.AppendLine("    private readonly global::System.Collections.Generic.HashSet<string> _modifiedProperties = [];");
            body.AppendLine("    private readonly global::System.Collections.Generic.HashSet<string> _collectionsModified = [];");
            body.AppendLine("    [global::System.Text.Json.Serialization.JsonIgnore] public global::System.Collections.Generic.IReadOnlySet<string> ModifiedProperties => _modifiedProperties;");
            body.AppendLine("    [global::System.Text.Json.Serialization.JsonIgnore] public global::System.Collections.Generic.IReadOnlySet<string> CollectionsModified => _collectionsModified;");
            body.AppendLine("    public void ResetModifiedProperties() { _modifiedProperties.Clear(); _collectionsModified.Clear(); }");
            body.AppendLine("    [global::System.Text.Json.Serialization.JsonIgnore] [global::System.ComponentModel.DataAnnotations.Schema.NotMapped] public bool IsNew { get; set; }");
        }

        foreach (var property in tracked)
        {
            if (!declared.Add("Set" + property.Name))
                continue;
            var type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));
            var record = IsDerivedEntity(entity) ? "" : $" _modifiedProperties.Add(nameof({property.Name}));";
            body.AppendLine($"    internal void Set{property.Name}({type} value) {{ if (global::System.Collections.Generic.EqualityComparer<{type}>.Default.Equals({property.Name}, value)) return; {property.Name} = value;{record} }}");
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
