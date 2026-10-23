using Pragmatic.Persistence.Sql;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     A member path below a JSON column (<c>[Convert&lt;SqlJson&lt;T, TContext&gt;&gt;]</c>): <c>d.Spec.Pages</c> is the JSON value at <c>$.Pages</c>,
///     as the member's kind (<c>->></c> with a cast on PostgreSQL, <c>json_extract</c> on SQLite, <c>JSON_VALUE</c> with a cast on SQL Server);
///     <c>d.Spec.Tags.Count</c> the array's length, <c>d.Spec.Tags.Contains(x)</c> a predicate over its elements. The JSON names are the
///     properties' names, or their <c>[JsonPropertyName]</c> (ASSUMPTION: the serializer context uses no naming policy).
/// </summary>
internal sealed partial class LambdaTranslator
{
    /// <summary>The JSON column and path the last member access translated to (<c>d.Spec</c>, <c>Pages</c>), for an update of that member; null otherwise.</summary>
    public (string Column, string Path)? LastJson { get; private set; }

    internal static bool IsJsonConverter(string? converter) => converter is not null && converter.StartsWith("global::Pragmatic.Persistence.Sql.SqlJson<", System.StringComparison.Ordinal);

    /// <summary>The JSON path of the segments below the column, and the type the path ends on; null with the error when a segment is not a member of the JSON type.</summary>
    private string? JsonPath(ColumnModel json, List<string> segments, int from, int to, SyntaxNode where, out ITypeSymbol? leaf)
        => JsonPath(json.Type, segments, from, to, where, out leaf);

    private string? JsonPath(ITypeSymbol root, List<string> segments, int from, int to, SyntaxNode where, out ITypeSymbol? leaf)
    {
        leaf = root;
        var names = new List<string>();
        for (var i = from; i < to; i++)
        {
            var type = leaf is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : leaf;
            var property = type?.GetMembers(segments[i]).OfType<IPropertySymbol>().FirstOrDefault();
            if (property is null)
            {
                Fail(where, $"'{segments[i]}' is not a property of {type?.Name}: a path into a JSON column names the serialized properties");
                return null;
            }

            var named = property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "JsonPropertyNameAttribute");
            names.Add(named is { ConstructorArguments.Length: > 0 } && named.ConstructorArguments[0].Value is string jsonName ? jsonName : property.Name);
            leaf = property.Type;
        }

        return string.Join(".", names);
    }

    /// <summary>The kind a JSON value is cast to, from the member's type: text, int, long, decimal, double, bool, datetime, uuid.</summary>
    private static string JsonKind(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum })
            return "int";
        return type?.SpecialType switch
        {
            SpecialType.System_Int32 or SpecialType.System_Int16 or SpecialType.System_Byte => "int",
            SpecialType.System_Int64 => "long",
            SpecialType.System_Decimal => "decimal",
            SpecialType.System_Double or SpecialType.System_Single => "double",
            SpecialType.System_Boolean => "bool",
            SpecialType.System_DateTime => "datetime",
            _ => type?.ToDisplayString() switch { "System.DateTimeOffset" => "datetime", "System.Guid" => "uuid", _ => "text" },
        };
    }

    /// <summary>A member path that ends inside a JSON column: the value at the path (or an array's Count), rendered per dialect.</summary>
    private string? JsonMember(string alias, ColumnModel json, List<string> segments, int from, MemberAccessExpressionSyntax member)
    {
        var column = $"{alias}.\"{json.Column}\"";
        var count = segments[segments.Count - 1] == "Count" && segments.Count - from > 1;
        var path = JsonPath(json, segments, from, count ? segments.Count - 1 : segments.Count, member, out var leaf);
        if (path is null)
            return null;
        LastColumn = null;
        LastJson = (column, path);
        if (count)
        {
            if (!IsCollection(leaf))
                return Fail(member, $"'{segments[segments.Count - 2]}' is not a collection: Count counts a JSON array");
            return Rendered(SqlCatalog.Find("JsonArrayLength", 2)!, [column, path], "json");
        }

        return Rendered(SqlCatalog.Find("JsonValue", 3)!, [column, path, JsonKind(leaf)], "json");
    }

    /// <summary><c>d.Spec.Tags.Contains(x)</c>: an element of a JSON array equal to the value.</summary>
    private string? JsonArrayContains(InvocationExpressionSyntax invocation, MemberAccessExpressionSyntax array, ExpressionSyntax item, string path)
    {
        if (Segments(array, out var casts) is not { } segments)
            return null;
        var located = JsonColumnAt(segments, casts, array);
        if (located is null)
            return null;
        var (alias, json, from) = located.Value;
        var jsonPath = JsonPath(json, segments, from, segments.Count, array, out var leaf);
        if (jsonPath is null)
            return null;
        if (!IsCollection(leaf))
            return Fail(invocation, $"'{segments[segments.Count - 1]}' is not a collection: Contains looks into a JSON array");
        var value = Translate(Strip(item), $"Captured.Argument({path}, 0)", asPredicate: false);
        if (value is null)
            return null;
        LastColumn = null;
        return Rendered(SqlCatalog.Find("JsonArrayContains", 3)!, [$"{alias}.\"{json.Column}\"", jsonPath, value], "json");
    }

    /// <summary>The element parameter of a JSON array's Any while its condition is translated, and the elements' type; null otherwise.</summary>
    private (IParameterSymbol Parameter, ITypeSymbol Type)? _jsonElement;

    /// <summary>
    ///     <c>d.Spec.Chapters.Any(c => c.Pages > 10)</c>: an element of a JSON array of objects meeting the condition, EXISTS over the elements;
    ///     inside the condition a member of the element is the JSON value at its path in the element (<c>je."value"</c>).
    /// </summary>
    private string? JsonArrayAny(InvocationExpressionSyntax invocation, MemberAccessExpressionSyntax array, SimpleLambdaExpressionSyntax lambda, string path, string entry = "JsonArrayAny")
    {
        if (Segments(array, out var casts) is not { } segments)
            return null;
        var located = JsonColumnAt(segments, casts, array);
        if (located is null)
            return null;
        var (alias, json, from) = located.Value;
        var jsonPath = JsonPath(json, segments, from, segments.Count, array, out var leaf);
        if (jsonPath is null)
            return null;
        var element = leaf is IArrayTypeSymbol arrayType ? arrayType.ElementType : (leaf as INamedTypeSymbol)?.TypeArguments.FirstOrDefault();
        if (!IsCollection(leaf) || element is null || lambda.Body is not ExpressionSyntax body || _model.GetDeclaredSymbol(lambda.Parameter) is not IParameterSymbol parameter)
            return Fail(invocation, $"'{segments[segments.Count - 1]}' is not a JSON array of objects: Any(e => condition) tests its elements");
        var outer = _jsonElement;
        _jsonElement = (parameter, element);
        var condition = Translate(body, $"Captured.Lambda(Captured.Argument({path}, 0))", asPredicate: true);
        _jsonElement = outer;
        if (condition is null)
            return null;
        LastColumn = null;
        return Rendered(SqlCatalog.Find(entry, 3)!, [$"{alias}.\"{json.Column}\"", jsonPath, condition], "json");
    }

    /// <summary>
    ///     The other forms over a JSON array: <c>Count(e => …)</c> and <c>Where(e => …).Count()</c> count the elements meeting the condition,
    ///     <c>Where(e => …).Any()</c> is Any's EXISTS, <c>Distinct().Count()</c> counts the distinct values of an array of values. Null when the
    ///     receiver is not a JSON array.
    /// </summary>
    private string? JsonArrayQuery(InvocationExpressionSyntax invocation, IMethodSymbol method, string path)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
            return null;
        var arguments = invocation.ArgumentList.Arguments;
        if (method.Name == "Count" && arguments.Count == 1 && arguments[0].Expression is SimpleLambdaExpressionSyntax counted && access.Expression is MemberAccessExpressionSyntax countedArray && IsJsonArray(countedArray))
            return JsonArrayAny(invocation, countedArray, counted, path, "JsonArrayCount");
        if (arguments.Count != 0 || access.Expression is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax inner } innerCall || inner.Expression is not MemberAccessExpressionSyntax array || !IsJsonArray(array))
            return null;
        var innerPath = $"Captured.Argument({path}, 0)";
        switch (inner.Name.Identifier.ValueText)
        {
            case "Where" when innerCall.ArgumentList.Arguments.Count == 1 && innerCall.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax where:
                return JsonArrayAny(innerCall, array, where, innerPath, method.Name == "Any" ? "JsonArrayAny" : "JsonArrayCount");
            case "Distinct" when method.Name == "Count" && innerCall.ArgumentList.Arguments.Count == 0:
            {
                if (Segments(array, out var casts) is not { } segments || JsonColumnAt(segments, casts, array) is not { } located)
                    return null;
                var (alias, json, from) = located;
                var jsonPath = JsonPath(json, segments, from, segments.Count, array, out _);
                if (jsonPath is null)
                    return null;
                LastColumn = null;
                return Rendered(SqlCatalog.Find("JsonArrayDistinctCount", 2)!, [$"{alias}.\"{json.Column}\"", jsonPath], "json");
            }

            default:
                return null;
        }
    }

    /// <summary>True when the member path starts at the element parameter of the JSON array's Any being translated.</summary>
    private bool IsOnJsonElement(MemberAccessExpressionSyntax member)
    {
        ExpressionSyntax current = member;
        while (current is MemberAccessExpressionSyntax access)
            current = access.Expression;
        return _jsonElement is { } element && current is IdentifierNameSyntax identifier && SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(identifier).Symbol, element.Parameter);
    }

    /// <summary>A member of a JSON array's element (<c>c.Pages</c>, <c>c.Author.Name</c>): the JSON value at its path in the element, as the member's kind.</summary>
    private string? JsonElementMember(MemberAccessExpressionSyntax member)
    {
        var names = new List<string>();
        ExpressionSyntax current = member;
        while (current is MemberAccessExpressionSyntax access)
        {
            names.Insert(0, access.Name.Identifier.ValueText);
            current = access.Expression;
        }

        var path = JsonPath(_jsonElement!.Value.Type, names, 0, names.Count, member, out var leaf);
        if (path is null)
            return null;
        LastColumn = null;
        return Rendered(SqlCatalog.Find("JsonValue", 3)!, ["je.\"value\"", path, JsonKind(leaf)], "json");
    }

    /// <summary>True for an array column on the row (<c>x.Tags</c> of <c>string[]</c>, <c>List&lt;Guid&gt;</c>…), not an array inside a JSON column.</summary>
    private bool IsArrayColumn(MemberAccessExpressionSyntax member)
        => IsOnParameter(member) && EntityModel.IsArrayColumn(_model.GetTypeInfo(member).Type!) && !IsJsonArray(member);

    /// <summary>
    ///     <c>x.Tags.Any(t =&gt; list.Contains(t))</c> on an array column, the list a value of the caller (a request's access scopes): an element in
    ///     common, <c>&amp;&amp;</c> with an array parameter on PostgreSQL, the list expanded over the JSON elements elsewhere.
    /// </summary>
    private string? ArrayOverlaps(InvocationExpressionSyntax invocation, MemberAccessExpressionSyntax array, string path)
    {
        const string Form = "an Any over an array column takes an element of a list of the caller's: x.Tags.Any(t => list.Contains(t))";
        if (invocation.ArgumentList.Arguments[0].Expression is not SimpleLambdaExpressionSyntax { Body: InvocationExpressionSyntax contains } lambda
            || _model.GetSymbolInfo(contains).Symbol is not IMethodSymbol { Name: "Contains" } method)
            return Fail(invocation, Form);
        var isStatic = method.IsStatic && method.ReducedFrom is null;
        var list = isStatic ? contains.ArgumentList.Arguments[0].Expression : (contains.Expression as MemberAccessExpressionSyntax)?.Expression;
        var item = contains.ArgumentList.Arguments[isStatic ? 1 : 0].Expression;
        if (list is null || !IsCapturedValue(list) || Strip(item) is not IdentifierNameSyntax element || element.Identifier.ValueText != lambda.Parameter.Identifier.ValueText)
            return Fail(invocation, Form);
        var column = Translate(array, $"Captured.Argument({path}, 0)", asPredicate: false);
        var text = ValueText(list, out _);
        if (column is null || text is null)
            return null;
        // The list in the tree: the Any's lambda, its body the Contains, the list its argument 0 (Enumerable.Contains) or its target (an instance Contains).
        var body = $"Captured.Body((global::System.Linq.Expressions.LambdaExpression)Captured.Argument({path}, 1))";
        Add($"Captured.List({(isStatic || method.ReducedFrom is not null ? $"Captured.Argument({body}, 0)" : $"Captured.Target({body})")})", "List", text, isConstant: false);
        LastColumn = null;
        return Rendered(SqlCatalog.Find("ArrayOverlaps", 2)!, [column, $"{{{_parameterPaths.Count - 1}}}"], "array");
    }

    /// <summary><c>x.Tags.Contains(v)</c> on an array column: an element equal to the value (<c>= ANY</c> on PostgreSQL, over the JSON elsewhere).</summary>
    private string? ArrayContains(MemberAccessExpressionSyntax array, ExpressionSyntax item, string path)
    {
        var column = Translate(array, path, asPredicate: false);
        var value = column is null ? null : Translate(Strip(item), $"Captured.Argument({path}, 0)", asPredicate: false);
        if (column is null || value is null)
            return null;
        LastColumn = null;
        return Rendered(SqlCatalog.Find("ArrayContains", 2)!, [column, value], "array");
    }

    /// <summary><c>x.Tags.Length</c> or <c>.Count</c> on an array column: the number of its elements.</summary>
    private string? ArrayLength(MemberAccessExpressionSyntax array, string path)
    {
        var column = Translate(array, path, asPredicate: false);
        if (column is null)
            return null;
        LastColumn = null;
        return Rendered(SqlCatalog.Find("ArrayLength", 1)!, [column], "array");
    }

    /// <summary>True when a member path on the parameter runs into a JSON column before its end (so a Contains on it is an array test, not an IN).</summary>
    private bool IsJsonArray(ExpressionSyntax receiver)
        => Strip(receiver) is MemberAccessExpressionSyntax member && IsOnParameter(member) && Segments(member, out var casts) is { } segments && JsonColumnAt(segments, casts, member) is { } at && at.From < segments.Count && IsCollection(_model.GetTypeInfo(member).Type);

    /// <summary>Where a member path enters a JSON column: the alias, the column, the index of the first segment below it; null when it does not (no error).</summary>
    private (string Alias, ColumnModel Json, int From)? JsonColumnAt(List<string> segments, List<INamedTypeSymbol?> casts, MemberAccessExpressionSyntax member)
    {
        var at = EntityAt(segments, member, out _);
        if (at is null)
            return null;
        var (entity, alias, start, _, _) = at.Value;
        for (var i = start; i < segments.Count; i++)
        {
            var model = Narrowed(entity, casts[i], member, out var narrowed);
            if (model is null)
                return null;
            if (model.Column(segments[i]) is { } column)
                return IsJsonConverter(column.Converter) && i < segments.Count - 1 ? (alias, column, i + 1) : null;
            var optional = false;
            var target = Hop(model, ref alias, ref optional, segments[i], narrowed ? casts[i]!.Name : null, member, wanted: false);
            if (target is null)
                return null;
            entity = target;
        }

        return null;
    }

    private static bool IsCollection(ITypeSymbol? type)
        => type is IArrayTypeSymbol || (type is INamedTypeSymbol named && (named.AllInterfaces.Any(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T) || named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T) && type.SpecialType != SpecialType.System_String);
}
