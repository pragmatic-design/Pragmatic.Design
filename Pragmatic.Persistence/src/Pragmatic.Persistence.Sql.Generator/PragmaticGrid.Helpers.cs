using System.Linq;
using System.Text;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     The grid's helpers, as Pragmatic's template writes them (<c>GridAdapterTemplate</c>): what a grid sent read as a CLR value (a filter that
///     arrived over HTTP is a list of <c>JsonElement</c>), the refusals of a field and of a clause, the width bound. The conversions are typed and
///     read the invariant culture; a <c>Guid</c> and a <c>DateTimeOffset</c> are parsed from their text, which <c>Convert.ChangeType</c> does not do.
/// </summary>
internal sealed partial class PragmaticGrid
{
    private void Helpers(StringBuilder text)
    {
        const string json = "global::System.Text.Json";
        var filterable = Fields.Where(f => f.Filterable).Select(f => PragmaticNames.Literal(f.Json.ToLowerInvariant())).Distinct().ToList();
        text.AppendLine(filterable.Count == 0
            ? "    private static bool IsFilterable(string field) => false;"
            : $"    private static bool IsFilterable(string field) => field.ToLowerInvariant() is {string.Join(" or ", filterable)};");
        text.AppendLine();
        text.AppendLine(Withheld.Count == 0
            ? $"    private static global::System.Exception RejectedField(string field) => new {Adapters}.GridFieldRejectedException(field, {Adapters}.GridFieldRejection.Unknown);"
            : $"    private static global::System.Exception RejectedField(string field) => new {Adapters}.GridFieldRejectedException(field, field.ToLowerInvariant() is {string.Join(" or ", Withheld.Select(PragmaticNames.Literal))} ? {Adapters}.GridFieldRejection.Withheld : {Adapters}.GridFieldRejection.Unknown);");
        text.AppendLine();
        text.AppendLine($"    private static global::System.Exception Malformed() => new {Adapters}.GridFilterRejectedException(null, {Adapters}.GridFilterRejection.Malformed);");
        text.AppendLine();
        text.AppendLine($"    private static global::System.Exception Unusable(string field) => new {Adapters}.GridFilterRejectedException(field, {Adapters}.GridFilterRejection.UnusableValue);");
        text.AppendLine();
        text.AppendLine($"    private static global::System.Exception Unsupported(string field, string? op) => new {Adapters}.GridFilterRejectedException(field, {Adapters}.GridFilterRejection.UnsupportedOperator, op);");
        text.AppendLine();
        text.AppendLine("    private static int CountedTerms(int terms)");
        text.AppendLine($"        => terms > {Adapters}.GridFilterTooLargeException.MaxTerms ? throw new {Adapters}.GridFilterTooLargeException(terms) : terms;");
        text.AppendLine();
        text.AppendLine("    /// <summary>A clause or a group, as a list: a nested group that arrived as JSON is an array element, not a list.</summary>");
        text.AppendLine("    private static global::System.Collections.Generic.IReadOnlyList<object?>? Group(object? value) => value switch");
        text.AppendLine("    {");
        text.AppendLine($"        {json}.JsonElement {{ ValueKind: {json}.JsonValueKind.Array }} element => global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(element.EnumerateArray(), e => (object?)e)),");
        text.AppendLine("        global::System.Collections.Generic.IReadOnlyList<object?> list => list,");
        text.AppendLine("        _ => null,");
        text.AppendLine("    };");
        text.AppendLine();
        text.AppendLine("    private static global::System.Collections.Generic.IReadOnlyList<object?> GroupAt(global::System.Collections.Generic.IReadOnlyList<object?> filter, int index)");
        text.AppendLine("        => Group(filter[index]) ?? throw Malformed();");
        text.AppendLine();
        text.AppendLine("    /// <summary>What a grid sent as a CLR value: a JsonElement as the thing it holds, a number as a long when it is one; anything else unchanged.</summary>");
        text.AppendLine("    private static object? Unwrap(object? value)");
        text.AppendLine("    {");
        text.AppendLine($"        if (value is not {json}.JsonElement element)");
        text.AppendLine("            return value;");
        text.AppendLine("        return element.ValueKind switch");
        text.AppendLine("        {");
        text.AppendLine("            // A string whose bytes are not UTF-8 stays the element: no column converts it.");
        text.AppendLine($"            {json}.JsonValueKind.String => ReadString(element) ?? (object)element,");
        text.AppendLine($"            {json}.JsonValueKind.True => true,");
        text.AppendLine($"            {json}.JsonValueKind.False => false,");
        text.AppendLine($"            {json}.JsonValueKind.Null or {json}.JsonValueKind.Undefined => null,");
        text.AppendLine($"            {json}.JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole : element.GetDouble(),");
        text.AppendLine("            _ => value,");
        text.AppendLine("        };");
        text.AppendLine("    }");
        text.AppendLine();
        text.AppendLine("    /// <summary>A JsonElement's text, validated only when read: an unreadable string is a value the adapter cannot use, not an exception out of it.</summary>");
        text.AppendLine($"    private static string? ReadString({json}.JsonElement element)");
        text.AppendLine("    {");
        text.AppendLine("        try");
        text.AppendLine("        {");
        text.AppendLine("            return element.GetString();");
        text.AppendLine("        }");
        text.AppendLine("        catch (global::System.InvalidOperationException)");
        text.AppendLine("        {");
        text.AppendLine("            return null;");
        text.AppendLine("        }");
        text.AppendLine("    }");
        text.AppendLine();
        text.AppendLine("    /// <summary>The text a clause compares against, null when there is none: an array is not a text, nor a string that could not be read.</summary>");
        text.AppendLine("    private static string? Text(object? value)");
        text.AppendLine("    {");
        text.AppendLine($"        if (Unwrap(value) is not {json}.JsonElement element)");
        text.AppendLine("            return global::System.Convert.ToString(Unwrap(value), global::System.Globalization.CultureInfo.InvariantCulture);");
        text.AppendLine($"        if (element.ValueKind is {json}.JsonValueKind.String or {json}.JsonValueKind.Array)");
        text.AppendLine("            return null;");
        text.AppendLine("        try");
        text.AppendLine("        {");
        text.AppendLine("            return element.GetRawText();");
        text.AppendLine("        }");
        text.AppendLine("        catch (global::System.InvalidOperationException)");
        text.AppendLine("        {");
        text.AppendLine("            return null;");
        text.AppendLine("        }");
        text.AppendLine("    }");
        text.AppendLine();
        text.AppendLine("    private static bool TryConvert<T>(object? value, out T result) where T : struct, global::System.IConvertible");
        text.AppendLine("    {");
        text.AppendLine("        result = default;");
        text.AppendLine("        if (Unwrap(value) is not global::System.IConvertible convertible)");
        text.AppendLine("            return false;");
        text.AppendLine("        try");
        text.AppendLine("        {");
        text.AppendLine("            result = (T)convertible.ToType(typeof(T), global::System.Globalization.CultureInfo.InvariantCulture);");
        text.AppendLine("            return true;");
        text.AppendLine("        }");
        text.AppendLine("        catch (global::System.Exception e) when (e is global::System.FormatException or global::System.InvalidCastException or global::System.OverflowException)");
        text.AppendLine("        {");
        text.AppendLine("            return false;");
        text.AppendLine("        }");
        text.AppendLine("    }");
        text.AppendLine();
        text.AppendLine("    private static bool TryGuid(object? value, out global::System.Guid result)");
        text.AppendLine("    {");
        text.AppendLine("        if (Unwrap(value) is global::System.Guid guid)");
        text.AppendLine("        {");
        text.AppendLine("            result = guid;");
        text.AppendLine("            return true;");
        text.AppendLine("        }");
        text.AppendLine("        return global::System.Guid.TryParse(Text(value), out result);");
        text.AppendLine("    }");
        text.AppendLine();
        text.AppendLine("    private static bool TryDateTimeOffset(object? value, out global::System.DateTimeOffset result)");
        text.AppendLine("    {");
        text.AppendLine("        if (Unwrap(value) is global::System.DateTimeOffset instant)");
        text.AppendLine("        {");
        text.AppendLine("            result = instant;");
        text.AppendLine("            return true;");
        text.AppendLine("        }");
        text.AppendLine("        return global::System.DateTimeOffset.TryParse(Text(value), global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.AssumeUniversal, out result);");
        text.AppendLine("    }");
        text.AppendLine();
        text.AppendLine("    /// <summary>An enum by name (ignoring case) or by a defined value.</summary>");
        text.AppendLine("    private static bool TryEnum<T>(object? value, out T result) where T : struct, global::System.Enum");
        text.AppendLine("    {");
        text.AppendLine("        result = default;");
        text.AppendLine("        switch (Unwrap(value))");
        text.AppendLine("        {");
        text.AppendLine("            case string name:");
        text.AppendLine("                return global::System.Enum.TryParse(name, ignoreCase: true, out result);");
        text.AppendLine("            case long whole:");
        text.AppendLine("                result = (T)global::System.Enum.ToObject(typeof(T), whole);");
        text.AppendLine("                return global::System.Enum.IsDefined(result);");
        text.AppendLine("            default:");
        text.AppendLine("                return false;");
        text.AppendLine("        }");
        text.AppendLine("    }");
    }
}
