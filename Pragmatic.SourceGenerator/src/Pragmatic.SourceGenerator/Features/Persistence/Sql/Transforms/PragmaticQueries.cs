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
///     Pragmatic's declared queries (<c>[Query&lt;TEntity, TResult&gt;]</c>) under the Sql engine. Pragmatic's generator implements them only with
///     EF Core (its <c>QueryFeature</c> sits behind the same gate as the entities' shape), so here the Sql engine declares their shape: the
///     interface (<c>IQuery</c>, or <c>IPagedQuery</c> with <c>Page</c>/<c>PageSize</c>), <c>Apply</c> (refused: the engine reads the declaration, not
///     an <c>IQueryable</c>), <c>ToSpecification()</c> (the filters as <c>Spec&lt;T&gt;.Where</c> leaves, which this generator compiles) and
///     <c>ToSqlOrdering()</c> (the sorts, as compiled keys). The rules are Pragmatic's (<c>QueryTransform</c>, <c>QueryApplyTemplate</c>): a
///     property is a filter with <c>[Filter]</c>, or when required, or when nullable; a string's default operator is Contains under
///     <c>[Filter]</c>, a collection's In. What the engine does not compile yet is named (<see cref="Unsupported" />) and the query refused.
///     This file is engine-neutral (linked by the documents generator): the ordering is each engine's (<c>PragmaticQueries.Sql.cs</c> here).
/// </summary>
internal sealed partial class PragmaticQuery
{
    private const string Attributes = "Pragmatic.Persistence.Query.Attributes.";

    private PragmaticQuery(INamedTypeSymbol type, INamedTypeSymbol entity, INamedTypeSymbol result)
    {
        Type = type;
        Entity = entity;
        Result = result;
    }

    public INamedTypeSymbol Type { get; }

    public INamedTypeSymbol Entity { get; }

    public INamedTypeSymbol Result { get; }

    public bool Paged { get; private set; }

    /// <summary>Why the engine does not compile this query yet; null when it does.</summary>
    public string? Unsupported { get; private set; }

    /// <summary>An engine's own reason not to compile the query (the documents engine: a projection it does not map), when there is none yet.</summary>
    internal void Refuse(string why) => Unsupported ??= why;

    private readonly List<QueryConditionModel> _conditions = [];
    private readonly List<(string Name, string Column, int Direction, int Priority)> _sorts = [];

    /// <summary>The sorts: the property, the entity's field it orders by, the default direction (-1: none), the priority.</summary>
    internal IReadOnlyList<(string Name, string Column, int Direction, int Priority)> Sorts => _sorts;

    /// <summary>The declared queries of the compilation on the Pragmatic entities <paramref name="engine" /> serves.</summary>
    public static List<PragmaticQuery> Read(Compilation compilation, PragmaticEngine engine)
    {
        var queries = new List<PragmaticQuery>();
        var engines = PragmaticEngines.Of(compilation);
        foreach (var type in PragmaticNames.AllTypes(compilation.GlobalNamespace).Where(t => t.DeclaringSyntaxReferences.Length > 0))
        {
            var attribute = type.GetAttributes().Select(a => a.AttributeClass).FirstOrDefault(c => c is { IsGenericType: true, Name: "QueryAttribute" }
                && c.ContainingNamespace.ToDisplayString() + "." == Attributes);
            if (attribute?.TypeArguments[0] is not INamedTypeSymbol entity || !PragmaticShape.IsPragmaticEntity(entity) || !engines.Serves(entity, engine))
                continue;
            var result = attribute.TypeArguments.Length > 1 ? attribute.TypeArguments[1] as INamedTypeSymbol ?? entity : entity;
            var query = new PragmaticQuery(type, entity, result);
            query.ReadMembers(type.GetAttributes().First(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute)));
            queries.Add(query);
        }

        return queries;
    }

    private void ReadMembers(AttributeData attribute)
    {
        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key is "Single" or "MapInMemory" && named.Value.Value is true)
                Unsupported ??= $"{named.Key} = true is not compiled yet";
            if (named.Key == "Paged" && named.Value.Value is true)
                Paged = true;
        }

        if (Type.GetMembers("Apply").Length > 0)
            Unsupported ??= "it writes its own Apply";
        var properties = Type.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public && p.GetMethod is not null).ToList();
        if (properties.Any(p => p.Name.Equals("Page", StringComparison.OrdinalIgnoreCase)) && properties.Any(p => p.Name.Equals("PageSize", StringComparison.OrdinalIgnoreCase)))
            Paged = true;
        foreach (var property in properties)
            ReadProperty(property);
    }

    private void ReadProperty(IPropertySymbol property)
    {
        if (property.Name.Equals("Page", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("PageSize", StringComparison.OrdinalIgnoreCase))
            return;
        var marks = property.GetAttributes().Select(a => a.AttributeClass?.Name).Where(n => n is not null).ToList();
        foreach (var mark in new[] { "SearchAcrossAttribute", "FromCurrentUserAttribute", "FromClockAttribute", "BindSpecificationAttribute", "ComplexFilterAttribute", "FilterGroupAttribute" })
        {
            if (marks.Contains(mark))
                Unsupported ??= $"[{mark.Substring(0, mark.Length - "Attribute".Length)}] on {property.Name} is not compiled yet";
        }

        if (property.Type is INamedTypeSymbol { Name: "Specification" or "ISpecification" })
        {
            Unsupported ??= $"the specification property {property.Name} is not compiled yet";
            return;
        }

        var sort = Mark(property, "SortAttribute");
        if (sort is not null)
        {
            var mapTo = Named(sort, "MapTo") as string;
            var column = mapTo ?? (property.Name.EndsWith("Sort", StringComparison.Ordinal) ? property.Name.Substring(0, property.Name.Length - 4) : property.Name);
            var direction = Named(sort, "DefaultDirection") is int d && d >= 0 ? d : -1;
            _sorts.Add((property.Name, column, direction, Named(sort, "Priority") is int p ? p : 0));
            return;
        }

        var filter = Mark(property, "FilterAttribute");
        var nullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated || property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        var required = property.IsRequired;
        if (filter is null && !required && !nullable)
            return; // an inert input: not a filter in Pragmatic's rules either
        var isString = Unwrap(property.Type).SpecialType == SpecialType.System_String;
        var collection = IsScalarCollection(property.Type);
        var explicitOperator = Named(filter, "Operator") is int op ? op : (int?)null;
        // FilterOperator: Equals 0, NotEquals 1, Contains 2, StartsWith 3, EndsWith 4, GreaterThan 5, GreaterOrEqual 6, LessThan 7, LessOrEqual 8, In 9, Between 10.
        var operatorKind = explicitOperator ?? (collection ? 9 : filter is not null && isString ? 2 : 0);
        var path = Named(filter, "MapTo") as string ?? property.Name;
        var ignoreCase = Named(filter, "IgnoreCase") is true && isString;
        var value = $"this.{property.Name}";
        var valueAccess = required || !nullable ? value : property.Type.IsValueType ? value + ".Value" : value + "!";
        var entityPath = $"e.{path}";
        string Comparison(string column, string v) => operatorKind switch
        {
            1 => $"{column} != {v}",
            2 => $"{column}.Contains({v})",
            3 => $"{column}.StartsWith({v})",
            4 => $"{column}.EndsWith({v})",
            5 => $"{column} > {v}",
            6 => $"{column} >= {v}",
            7 => $"{column} < {v}",
            8 => $"{column} <= {v}",
            9 => $"{v}.Contains({column})",
            10 => "",
            _ => $"{column} == {v}",
        };
        var condition = ignoreCase ? Comparison(entityPath + ".ToLower()", valueAccess + ".ToLower()") : Comparison(entityPath, valueAccess);
        if (condition.Length == 0)
        {
            Unsupported ??= $"Between on {property.Name} is not compiled yet";
            return;
        }

        var dereferences = ignoreCase || operatorKind is 2 or 3 or 4;
        if (dereferences && EntityPathNullable(path))
            condition = $"{entityPath} != null && {condition}";
        var guard = collection ? $"{value} != null && global::System.Linq.Enumerable.Any({value})" : required ? null : nullable ? $"{value} is not null" : $"{value} != default";
        _conditions.Add(new QueryConditionModel(guard, condition));
    }

    internal string EntityName => "global::" + Entity.ToDisplayString();

    internal string ResultName => "global::" + Result.ToDisplayString();

    /// <summary>
    ///     The query's shape (hint name, text): its interface, the refused Apply, the filters as a specification, then the engine's ordering
    ///     (<paramref name="ordering" />), written only for a query the engine compiles. <paramref name="artifact" /> names the file.
    /// </summary>
    internal (string Hint, string Source) ShapeText(string package, string engine, string artifact, QueryOrderingModel? ordering)
    {
        var model = new QueryShapeModel(
            VirtualFolderHints.ForType(Type.Name, artifact, Type.ContainingNamespace.ToDisplayString()),
            package,
            engine,
            Type.ContainingNamespace.IsGlobalNamespace ? null : Type.ContainingNamespace.ToDisplayString(),
            Type.Name,
            Paged,
            EntityName,
            ResultName,
            Unsupported is null,
            _conditions.ToEquatableArray(),
            ordering);
        return (model.Hint, new QueryShapeTemplate(model).ToSourceText().ToString());
    }

    private bool EntityPathNullable(string path)
        => !path.Contains('.') && Entity.GetMembers(path).OfType<IPropertySymbol>().FirstOrDefault() is { Type.NullableAnnotation: NullableAnnotation.Annotated };

    private static AttributeData? Mark(IPropertySymbol property, string name)
        => property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == Attributes + name);

    private static object? Named(AttributeData? attribute, string name)
        => attribute?.NamedArguments.FirstOrDefault(n => n.Key == name).Value.Value;

    private static ITypeSymbol Unwrap(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    private static bool IsScalarCollection(ITypeSymbol type)
        => type.SpecialType != SpecialType.System_String && (type is IArrayTypeSymbol || type is INamedTypeSymbol { IsGenericType: true, Name: "List" or "IReadOnlyList" or "IEnumerable" or "ICollection" or "IReadOnlyCollection" or "HashSet" });
}
