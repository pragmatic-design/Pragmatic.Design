using System.Globalization;
using System.Text.Json;

namespace Pragmatic.Persistence.Sql.Shapes;

/// <summary>
///     A client's filter as a tree: a comparison of a member with a value, a conjunction, a disjunction, a negation. Read from
///     DevExtreme's arrays by <see cref="Parse" /> (<c>["Total", ">", 100]</c>, <c>["Number", "ORD-1"]</c> for an equality,
///     <c>[[…], "and", […], "or", […]]</c> (one connective per group, as DevExtreme writes them), <c>["!", […]]</c>), or built in code.
/// </summary>
public abstract record SqlShapeFilter
{
    public sealed record Comparison(string Field, SqlShapeOperator Operator, object? Value) : SqlShapeFilter;

    public sealed record And(IReadOnlyList<SqlShapeFilter> Terms) : SqlShapeFilter;

    public sealed record Or(IReadOnlyList<SqlShapeFilter> Terms) : SqlShapeFilter;

    public sealed record Not(SqlShapeFilter Inner) : SqlShapeFilter;

    /// <summary>DevExtreme's filter array as a tree; a <see cref="SqlShapeException" /> when the array is not one.</summary>
    public static SqlShapeFilter Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
            throw new SqlShapeException("A filter is an array: [field, operator, value], [field, value], [filter, \"and\"/\"or\", filter, …] or [\"!\", filter].");
        var items = element.EnumerateArray().ToList();
        if (items[0].ValueKind == JsonValueKind.String)
        {
            var head = items[0].GetString()!;
            if (head == "!" && items.Count == 2)
                return new Not(Parse(items[1]));
            if (items.Count == 2)
                return new Comparison(head, SqlShapeOperator.Equal, ParseValue(items[1]));
            if (items.Count == 3 && items[1].ValueKind == JsonValueKind.String)
                return new Comparison(head, ParseOperator(items[1].GetString()!), ParseValue(items[2]));
            throw new SqlShapeException($"The filter on '{head}' is not [field, operator, value] or [field, value].");
        }

        // A group: filters joined by one connective ("and" by default when adjacent).
        var terms = new List<SqlShapeFilter>();
        string? connective = null;
        foreach (var item in items)
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var word = item.GetString()!.ToLowerInvariant();
                if (word is not ("and" or "or"))
                    throw new SqlShapeException($"'{word}' is not a connective (and, or).");
                if (connective is not null && connective != word)
                    throw new SqlShapeException("A filter group mixes 'and' and 'or': nest the groups instead.");
                connective = word;
                continue;
            }

            terms.Add(Parse(item));
        }

        if (terms.Count == 1)
            return terms[0];
        return connective == "or" ? new Or(terms) : new And(terms);
    }

    /// <summary>DevExtreme's sort array (<c>[{"selector": "Total", "desc": true}, "Number"]</c>) as sorts.</summary>
    public static IReadOnlyList<SqlShapeSort> ParseSort(JsonElement element)
    {
        var sorts = new List<SqlShapeSort>();
        if (element.ValueKind == JsonValueKind.Object || element.ValueKind == JsonValueKind.String)
            element = JsonDocument.Parse("[" + element.GetRawText() + "]").RootElement;
        if (element.ValueKind != JsonValueKind.Array)
            throw new SqlShapeException("A sort is an array of {\"selector\": field, \"desc\": bool} or of field names.");
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                sorts.Add(new SqlShapeSort(item.GetString()!, false));
            else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("selector", out var selector) && selector.ValueKind == JsonValueKind.String)
                sorts.Add(new SqlShapeSort(selector.GetString()!, item.TryGetProperty("desc", out var desc) && desc.ValueKind == JsonValueKind.True));
            else
                throw new SqlShapeException("A sort item is a field name or {\"selector\": field, \"desc\": bool}.");
        }

        return sorts;
    }

    private static SqlShapeOperator ParseOperator(string text) => text.ToLowerInvariant() switch
    {
        "=" => SqlShapeOperator.Equal,
        "<>" or "!=" => SqlShapeOperator.NotEqual,
        ">" => SqlShapeOperator.Greater,
        ">=" => SqlShapeOperator.GreaterOrEqual,
        "<" => SqlShapeOperator.Less,
        "<=" => SqlShapeOperator.LessOrEqual,
        "contains" => SqlShapeOperator.Contains,
        "notcontains" => SqlShapeOperator.NotContains,
        "startswith" => SqlShapeOperator.StartsWith,
        "endswith" => SqlShapeOperator.EndsWith,
        _ => throw new SqlShapeException($"'{text}' is not a filter operator (=, <>, >, >=, <, <=, contains, notcontains, startswith, endswith)."),
    };

    /// <summary>A JSON value as it comes: text, a number (as decimal, converted to the column's kind at bind time), a boolean, null.</summary>
    private static object? ParseValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new SqlShapeException($"A filter value is text, a number, a boolean or null; not {element.ValueKind}."),
    };

    /// <summary>A filter value converted to the column's kind (text to a Guid, a date, a number; a number to the column's width), for the parameter.</summary>
    internal static object? Convert(object? value, SqlParamKind kind, string field)
    {
        if (value is null)
            return null;
        try
        {
            var text = value as string;
            return kind switch
            {
                SqlParamKind.Guid => value is Guid g ? g : Guid.Parse(text ?? value.ToString()!),
                SqlParamKind.String => text ?? System.Convert.ToString(value, CultureInfo.InvariantCulture),
                SqlParamKind.Int32 => System.Convert.ToInt32(value, CultureInfo.InvariantCulture),
                SqlParamKind.Int64 => System.Convert.ToInt64(value, CultureInfo.InvariantCulture),
                SqlParamKind.Decimal => System.Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                SqlParamKind.Double => System.Convert.ToDouble(value, CultureInfo.InvariantCulture),
                SqlParamKind.Boolean => value is bool b ? b : text is not null ? bool.Parse(text) : System.Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
                SqlParamKind.DateTimeOffset => value is DateTimeOffset dto ? dto : DateTimeOffset.Parse(text ?? value.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                SqlParamKind.DateTime => value is DateTime dt ? dt : DateTime.Parse(text ?? value.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                SqlParamKind.DateOnly => value is DateOnly d ? d : DateOnly.Parse(text ?? value.ToString()!, CultureInfo.InvariantCulture),
                SqlParamKind.TimeOnly => value is TimeOnly t ? t : TimeOnly.Parse(text ?? value.ToString()!, CultureInfo.InvariantCulture),
                _ => value,
            };
        }
        catch (Exception e) when (e is FormatException or OverflowException or InvalidCastException)
        {
            throw new SqlShapeException($"The value '{value}' for '{field}' is not a {kind}.");
        }
    }
}
