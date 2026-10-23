using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     Pragmatic's grid adapters (<c>[GridAdapter&lt;TEntity&gt;]</c>) under the Sql engine. Pragmatic's generator implements them only with EF
///     Core (its <c>QueryFeature</c> sits behind the same gate as the entities' shape), so here the Sql engine declares them (its <c>Source</c>):
///     the DevExpress filter read into a specification whose leaves are <c>Spec&lt;T&gt;.Where</c> calls this generator compiles, one per field
///     and operator; the sort into compiled keys; a page read through the session. The fields are Pragmatic's (<c>GridAdapterTransform</c>): the
///     entity's public scalar properties as the class declares them, <c>[GridField]</c> aliases and restrictions, <c>[GridExclude]</c>, never a
///     sensitive name (<c>SensitiveGridFieldNames</c>, linked from Pragmatic). What the engine does not compile yet is named (<see cref="Omitted" />).
/// </summary>
internal sealed partial class PragmaticGrid
{
    private const string Attributes = "Pragmatic.Persistence.Query.Attributes.";

    /// <summary>The categories Pragmatic's adapter filters by (<c>PropertyTypeCategory</c>): one family of operators each.</summary>
    internal enum Category { String, Boolean, Numeric, DateTime, Guid, Enum, Unknown }

    /// <summary>A field of the grid: its name on the wire, the property, how it compares, what the declaration allows.</summary>
    internal sealed class Field(string json, string property, Category kind, string type, bool nullable, bool filterable, bool sortable, IReadOnlyList<string>? operators)
    {
        public string Json { get; } = json;

        public string Property { get; } = property;

        public Category Kind { get; } = kind;

        public string Type { get; } = type;

        public bool Nullable { get; } = nullable;

        public bool Filterable { get; } = filterable;

        public bool Sortable { get; } = sortable;

        public IReadOnlyList<string>? Operators { get; } = operators;
    }

    private PragmaticGrid(INamedTypeSymbol type, INamedTypeSymbol entity)
    {
        Type = type;
        Entity = entity;
    }

    public INamedTypeSymbol Type { get; }

    public INamedTypeSymbol Entity { get; }

    public bool Nested { get; private set; } = true;

    public List<Field> Fields { get; } = [];

    /// <summary>The properties a client may not name, refused as withheld rather than unknown, as Pragmatic's adapter refuses them.</summary>
    public List<string> Withheld { get; } = [];

    /// <summary>What the declaration asks that the Sql engine does not compile yet, each reported (PRAG3034).</summary>
    public List<string> Omitted { get; } = [];

    /// <summary>The grid adapters of the compilation on the Pragmatic entities <paramref name="engine" /> serves.</summary>
    public static List<PragmaticGrid> Read(Compilation compilation, PragmaticEngine engine)
    {
        var grids = new List<PragmaticGrid>();
        var engines = PragmaticEngines.Of(compilation);
        foreach (var type in PragmaticNames.AllTypes(compilation.GlobalNamespace).Where(t => t.DeclaringSyntaxReferences.Length > 0))
        {
            var attribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass is { IsGenericType: true, Name: "GridAdapterAttribute" } c
                && c.ContainingNamespace.ToDisplayString() + "." == Attributes);
            if (attribute?.AttributeClass!.TypeArguments[0] is not INamedTypeSymbol entity || !PragmaticShape.IsPragmaticEntity(entity) || !engines.Serves(entity, engine))
                continue;
            var grid = new PragmaticGrid(type, entity);
            grid.ReadDeclaration(attribute);
            grids.Add(grid);
        }

        return grids;
    }

    private void ReadDeclaration(AttributeData attribute)
    {
        // GridFramework: DevExpress 1, PrimeNG 2, Both 3 (the default).
        var framework = Named(attribute, "Framework") is int f ? f : 3;
        if ((framework & 2) != 0)
            Omitted.Add("PrimeNG (Framework includes GridFramework.PrimeNG): only DevExpress is compiled");
        if (Named(attribute, "SupportNestedFilters") is false)
            Nested = false;

        var excluded = new HashSet<string>(Type.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == Attributes + "GridExcludeAttribute")
            .Select(a => a.ConstructorArguments.FirstOrDefault().Value as string).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!), StringComparer.OrdinalIgnoreCase);
        var properties = AllProperties(Entity).ToList();
        var jsonNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // [GridField] first, as Pragmatic reads them: one pair of methods per property, the first alias wins.
        foreach (var custom in Type.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == Attributes + "GridFieldAttribute"))
        {
            if (custom.ConstructorArguments.FirstOrDefault().Value is not string json || json.Length == 0)
                continue;
            var path = Named(custom, "Property") as string ?? char.ToUpperInvariant(json[0]) + json.Substring(1);
            if (path.Contains('.'))
            {
                Omitted.Add($"the field '{json}' ({path}): a path through a navigation is not compiled yet, and the field is refused as unknown");
                continue;
            }

            var property = properties.FirstOrDefault(p => p.Name.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (property is null || Pragmatic.Contracts.SensitiveGridFieldNames.IsSensitive(property.Name) || !mapped.Add(property.Name))
                continue;
            Fields.Add(FieldOf(json, property, Named(custom, "Filterable") is not false, Named(custom, "Sortable") is not false, NamedArray(custom, "AllowedOperators")));
            jsonNames.Add(json);
        }

        foreach (var property in properties)
        {
            if (excluded.Contains(property.Name) || Pragmatic.Contracts.SensitiveGridFieldNames.IsSensitive(property.Name) || IsNavigation(property.Type))
                continue;
            var json = char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
            if (jsonNames.Contains(json) || !mapped.Add(property.Name))
                continue;
            Fields.Add(FieldOf(json, property, true, true, null));
            jsonNames.Add(json);
        }

        var filterable = new HashSet<string>(Fields.Where(x => x.Filterable).Select(x => x.Json), StringComparer.OrdinalIgnoreCase);
        Withheld.AddRange(properties.Select(p => p.Name).Where(n => Pragmatic.Contracts.SensitiveGridFieldNames.IsSensitive(n) && !filterable.Contains(n))
            .Select(n => n.ToLowerInvariant()).Distinct());
    }

    private static Field FieldOf(string json, IPropertySymbol property, bool filterable, bool sortable, IReadOnlyList<string>? operators)
    {
        var type = Unwrap(property.Type);
        var nullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated || property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        return new Field(json, property.Name, CategoryOf(type), type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), nullable, filterable, sortable, operators);
    }

    private static Category CategoryOf(ITypeSymbol type)
    {
        var name = type.ToDisplayString();
        return type.SpecialType switch
        {
            SpecialType.System_String => Category.String,
            SpecialType.System_Boolean => Category.Boolean,
            SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal => Category.Numeric,
            _ when name is "System.DateTime" or "System.DateTimeOffset" => Category.DateTime,
            _ when name == "System.Guid" => Category.Guid,
            _ when type.TypeKind == TypeKind.Enum => Category.Enum,
            _ => Category.Unknown,
        };
    }

    /// <summary>A navigation or a complex type, which Pragmatic's adapter never exposes: a collection, or a class other than string.</summary>
    private static bool IsNavigation(ITypeSymbol type)
        => type is INamedTypeSymbol named && ((named.IsGenericType && named.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
            || (named.TypeKind == TypeKind.Class && named.SpecialType == SpecialType.None));

    private static IEnumerable<IPropertySymbol> AllProperties(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsIndexer: false, GetMethod: not null })
                    yield return member;
            }
        }
    }

    private static object? Named(AttributeData attribute, string name)
        => attribute.NamedArguments.FirstOrDefault(n => n.Key == name).Value.Value;

    private static IReadOnlyList<string>? NamedArray(AttributeData attribute, string name)
        => attribute.NamedArguments.FirstOrDefault(n => n.Key == name).Value is { Kind: TypedConstantKind.Array } array
            ? array.Values.Select(v => v.Value as string).Where(v => v is not null).Select(v => v!).ToList()
            : null;

    private static ITypeSymbol Unwrap(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;
}
