using System.Linq.Expressions;
using System.Reflection;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>
///     The runtime twin of the generator's JSON paths: a member path below a column marked <see cref="SqlColumn.IsJson" /> is the JSON
///     value at the path (cast to the member's kind), an array's Count its length, an array's Contains a predicate over its elements.
///     The JSON names are the members' names, or the <c>[JsonPropertyName]</c> the generator read for them (<see cref="SqlColumn.JsonNames" />). An array
///     column (<see cref="SqlParamKind.Array" />: a native array on PostgreSQL, JSON text elsewhere) answers Contains and Length/Count the same way.
/// </summary>
internal sealed partial class ExpressionTranslator
{
    private static readonly SqlCatalog.Entry JsonValueEntry = SqlCatalog.Find("JsonValue", 3)!;
    private static readonly SqlCatalog.Entry JsonArrayLengthEntry = SqlCatalog.Find("JsonArrayLength", 2)!;
    private static readonly SqlCatalog.Entry JsonArrayContainsEntry = SqlCatalog.Find("JsonArrayContains", 3)!;
    private static readonly SqlCatalog.Entry ArrayContainsEntry = SqlCatalog.Find("ArrayContains", 2)!;
    private static readonly SqlCatalog.Entry ArrayOverlapsEntry = SqlCatalog.Find("ArrayOverlaps", 2)!;
    private static readonly SqlCatalog.Entry ArrayLengthEntry = SqlCatalog.Find("ArrayLength", 1)!;
    private static readonly SqlCatalog.Entry JsonArrayAnyEntry = SqlCatalog.Find("JsonArrayAny", 3)!;
    private static readonly SqlCatalog.Entry JsonArrayCountEntry = SqlCatalog.Find("JsonArrayCount", 3)!;
    private static readonly SqlCatalog.Entry JsonArrayDistinctCountEntry = SqlCatalog.Find("JsonArrayDistinctCount", 2)!;

    /// <summary>An element of a JSON array as the generator names it (<c>je."value"</c>): the column a nested translator's member paths read below.</summary>
    private const string JsonElementColumn = "value";

    /// <summary>Set on the nested translator of a JSON array's Any: a member path on its parameter is a JSON path below the element.</summary>
    private SqlColumn? _jsonElement;

    /// <summary>
    ///     A JSON array of the root entity tested or counted element by element: <c>Any(c => …)</c> and <c>Where(c => …).Any()</c> (EXISTS),
    ///     <c>Count(c => …)</c> and <c>Where(c => …).Count()</c>, <c>Distinct().Count()</c> over an array of values; null when the receiver is not one.
    /// </summary>
    private string? JsonArrayAny(MethodCallExpression call)
    {
        var name = call.Method.Name;
        Expression source;
        LambdaExpression? lambda = null;
        var distinct = false;
        if (call.Arguments.Count == 2 && name is "Any" or "Count")
        {
            source = call.Arguments[0];
            lambda = Strip(call.Arguments[1]) as LambdaExpression;
        }
        else if (call.Arguments.Count == 1 && Strip(call.Arguments[0]) is MethodCallExpression { Method.Name: "Where" or "Distinct" } inner && inner.Method.DeclaringType == typeof(Enumerable))
        {
            source = inner.Arguments[0];
            if (inner.Method.Name == "Where" && inner.Arguments.Count == 2)
                lambda = Strip(inner.Arguments[1]) as LambdaExpression;
            else
                distinct = inner.Method.Name == "Distinct" && inner.Arguments.Count == 1;
        }
        else
        {
            return null;
        }

        if (Strip(source) is not MemberExpression array || Segments(array) is not { Count: > 1 } segments || _map.Column(segments[0]) is not { IsJson: true } json || !IsCollectionType(array.Type))
            return null;
        var column = $"{_alias}.\"{json.Column}\"";
        var arrayChain = MemberChain(array);
        var jsonPath = JsonPath(arrayChain, 1, segments.Count, json);
        if (distinct)
        {
            if (name != "Count")
                return null;
            LastColumn = null;
            return JsonArrayDistinctCountEntry.Render(_dialect, [column, jsonPath]);
        }

        if (lambda is null)
            return null;
        var element = new ExpressionTranslator(this, _map, lambda.Parameters[0], "je") { _jsonElement = json.ElementAt(ClrPath(arrayChain, 1, segments.Count), JsonElementColumn) };
        var condition = element.Translate(lambda.Body, asPredicate: true);
        LastColumn = null;
        return (name == "Any" ? JsonArrayAnyEntry : JsonArrayCountEntry).Render(_dialect, [column, jsonPath, condition]);
    }

    /// <summary><c>x.Tags.Contains(v)</c> on an array column of the root entity (<c>string[]</c>, <c>List&lt;Guid&gt;</c>…); null when the receiver is not one.</summary>
    private string? ArrayColumnContains(Expression receiver, Expression item)
    {
        if (Strip(receiver) is not MemberExpression array || Segments(array) is not { Count: 1 } segments || _map.Column(segments[0]) is not { Kind: SqlParamKind.Array } column)
            return null;
        LastColumn = null;
        return ArrayContainsEntry.Render(_dialect, [$"{_alias}.\"{column.Column}\"", Translate(item, false)]);
    }

    /// <summary><c>x.Tags.Any(t =&gt; list.Contains(t))</c> on an array column of the root entity, the list a value of the caller's; null when the receiver is not one.</summary>
    private string? ArrayColumnOverlaps(MethodCallExpression call)
    {
        if (call.Method.Name != "Any" || call.Arguments.Count != 2 || Strip(call.Arguments[0]) is not MemberExpression array || Segments(array) is not { Count: 1 } segments
            || _map.Column(segments[0]) is not { Kind: SqlParamKind.Array } column)
            return null;
        if (Strip(call.Arguments[1]) is not LambdaExpression { Body: MethodCallExpression { Method.Name: "Contains" } contains } lambda)
            throw Unsupported(call, "an Any over an array column takes an element of a list of the caller's: x.Tags.Any(t => list.Contains(t))");
        var (list, item) = contains.Object is null ? (contains.Arguments[0], contains.Arguments[1]) : (contains.Object, contains.Arguments[0]);
        if (Strip(item) != lambda.Parameters[0] || DependsOnParameter(list))
            throw Unsupported(call, "an Any over an array column takes an element of a list of the caller's: x.Tags.Any(t => list.Contains(t))");
        LastColumn = null;
        return ArrayOverlapsEntry.Render(_dialect, [$"{_alias}.\"{column.Column}\"", Parameter(_capture(list, SqlCaptureKind.List, "", "", null), SqlParamKind.List)]);
    }

    /// <summary><c>x.Tags.Length</c> or <c>.Count</c> on an array column: the number of its elements.</summary>
    private string ArrayColumnLength(string alias, SqlColumn column)
    {
        LastColumn = null;
        return ArrayLengthEntry.Render(_dialect, [$"{alias}.\"{column.Column}\""]);
    }

    /// <summary>The value at the path below the JSON column (the chain's members from <paramref name="from" /> on), or an array's Count.</summary>
    private string JsonMember(MemberExpression member, string alias, SqlColumn json, int from)
    {
        var chain = MemberChain(member);
        var count = member.Member.Name == "Count" && chain.Count - from > 1 && IsCollectionType(((PropertyInfo)chain[chain.Count - 2]).PropertyType);
        var path = JsonPath(chain, from, count ? chain.Count - 1 : chain.Count, json);
        var column = $"{alias}.\"{json.Column}\"";
        LastColumn = null;
        return count ? JsonArrayLengthEntry.Render(_dialect, [column, path]) : JsonValueEntry.Render(_dialect, [column, path, JsonKind(member.Type)]);
    }

    /// <summary><c>d.Spec.Tags.Contains(x)</c> on a JSON array of the root entity; null when the receiver is not one.</summary>
    private string? JsonArrayContains(Expression receiver, Expression item)
    {
        if (Strip(receiver) is not MemberExpression array || Segments(array) is not { Count: > 1 } segments || _map.Column(segments[0]) is not { IsJson: true } json || !IsCollectionType(array.Type))
            return null;
        var path = JsonPath(MemberChain(array), 1, segments.Count, json);
        LastColumn = null;
        return JsonArrayContainsEntry.Render(_dialect, [$"{_alias}.\"{json.Column}\"", path, Translate(item, false)]);
    }

    private static List<MemberInfo> MemberChain(MemberExpression member)
    {
        var chain = new List<MemberInfo>();
        for (Expression? node = member; node is MemberExpression m; node = m.Expression is null ? null : Strip(m.Expression))
            chain.Insert(0, m.Member);
        return chain;
    }

    /// <summary>The JSON path of the chain's members from <paramref name="from" /> to <paramref name="to" />, named as <paramref name="json" /> says.</summary>
    private static string JsonPath(List<MemberInfo> chain, int from, int to, SqlColumn json)
    {
        var names = new string[to - from];
        var path = "";
        for (var i = from; i < to; i++)
        {
            path = i == from ? chain[i].Name : path + "." + chain[i].Name;
            names[i - from] = json.JsonName(path, chain[i].Name);
        }

        return string.Join(".", names);
    }

    /// <summary>The members' own names, from <paramref name="from" /> to <paramref name="to" />: the key <see cref="SqlColumn.JsonNames" /> uses.</summary>
    private static string ClrPath(List<MemberInfo> chain, int from, int to)
        => string.Join(".", chain.Skip(from).Take(to - from).Select(m => m.Name));

    private static string JsonKind(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
            return "int";
        return type == typeof(int) || type == typeof(short) || type == typeof(byte) ? "int"
            : type == typeof(long) ? "long"
            : type == typeof(decimal) ? "decimal"
            : type == typeof(double) || type == typeof(float) ? "double"
            : type == typeof(bool) ? "bool"
            : type == typeof(DateTime) || type == typeof(DateTimeOffset) ? "datetime"
            : type == typeof(Guid) ? "uuid"
            : "text";
    }

    private static bool IsCollectionType(Type type) => type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type); // no interface enumeration: it would need the type's members kept for trimming
}
