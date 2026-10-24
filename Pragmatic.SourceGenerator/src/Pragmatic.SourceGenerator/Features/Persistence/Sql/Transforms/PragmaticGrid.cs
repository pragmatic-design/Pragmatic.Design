using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

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

    private PragmaticGrid(INamedTypeSymbol type, INamedTypeSymbol entity)
    {
        Type = type;
        Entity = entity;
    }

    public INamedTypeSymbol Type { get; }

    public INamedTypeSymbol Entity { get; }

    public bool Nested { get; private set; } = true;

    public List<GridFieldModel> Fields { get; } = [];

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

    /// <summary>A field: a <c>[GridField(AllowedOperators)]</c> restriction is kept with its aliases, as the clause checks it.</summary>
    private static GridFieldModel FieldOf(string json, IPropertySymbol property, bool filterable, bool sortable, IReadOnlyList<string>? operators)
    {
        var type = Unwrap(property.Type);
        var nullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated || property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        var admitted = operators is { Count: > 0 } ? Aliases(operators) : [];
        return new GridFieldModel(json, property.Name, KindOf(type), type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), nullable, filterable, sortable, admitted.ToEquatableArray());
    }

    private static GridFieldKind KindOf(ITypeSymbol type)
    {
        var name = type.ToDisplayString();
        return type.SpecialType switch
        {
            SpecialType.System_String => GridFieldKind.String,
            SpecialType.System_Boolean => GridFieldKind.Boolean,
            SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal => GridFieldKind.Numeric,
            _ when name is "System.DateTime" or "System.DateTimeOffset" => GridFieldKind.DateTime,
            _ when name == "System.Guid" => GridFieldKind.Guid,
            _ when type.TypeKind == TypeKind.Enum => GridFieldKind.Enum,
            _ => GridFieldKind.Unknown,
        };
    }

    private string EntityName => "global::" + Entity.ToDisplayString();

    /// <summary>
    ///     The adapter's half an engine declares (hint name, text): the specification and its parsing (engine-neutral, linked by the documents
    ///     generator), the page read (<paramref name="load" />) and the sort's ordering (<paramref name="ordering" />) the engine's own;
    ///     <paramref name="artifact" /> names the file.
    /// </summary>
    internal (string Hint, string Source) SourceText(string package, string engine, string artifact, GridLoadModel load, GridOrderingModel ordering)
    {
        var model = new GridAdapterModel(
            VirtualFolderHints.ForType(Type.Name, artifact, Type.ContainingNamespace.ToDisplayString()),
            package,
            engine,
            Type.ContainingNamespace.IsGlobalNamespace ? null : Type.ContainingNamespace.ToDisplayString(),
            Type.Name,
            Entity.Name,
            EntityName,
            Nested,
            Fields.ToEquatableArray(),
            Withheld.ToEquatableArray(),
            load,
            ordering);
        return (model.Hint, new GridAdapterTemplate(model).ToSourceText().ToString());
    }

    /// <summary>The properties a compiled order key is built for: the sortable fields', then the entity's key, the ordering's fallback.</summary>
    internal IEnumerable<string> OrderKeyColumns() => Fields.Where(f => f.Sortable).Select(f => f.Property).Append("PersistenceId").Distinct();

    /// <summary>
    ///     The operator names a <c>[GridField(AllowedOperators)]</c> restriction covers, with their aliases, as Pragmatic's template expands them:
    ///     a restriction to "equals" also admits "=" and "==".
    /// </summary>
    private static List<string> Aliases(IReadOnlyList<string> allowed)
    {
        string[][] groups =
        [
            ["=", "==", "equals"], ["<>", "!=", "notequals"], ["contains"], ["notcontains"], ["startswith"], ["endswith"],
            [">", "gt", "greaterthan"], [">=", "gte", "greaterthanorequal"], ["<", "lt", "lessthan"], ["<=", "lte", "lessthanorequal"],
        ];
        var tokens = new List<string>();
        foreach (var name in allowed.Select(a => a.ToLowerInvariant()))
        {
            var group = groups.FirstOrDefault(g => Array.IndexOf(g, name) >= 0) ?? [name];
            tokens.AddRange(group.Where(t => !tokens.Contains(t)));
        }

        return tokens;
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
