using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;
using System.Linq;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     The grid's helpers, as Pragmatic's template writes them (<c>GridAdapterTemplate</c>): what a grid sent read as a CLR value (a filter that
///     arrived over HTTP is a list of <c>JsonElement</c>), the refusals of a field and of a clause, the width bound. The conversions are typed and
///     read the invariant culture; a <c>Guid</c> and a <c>DateTimeOffset</c> are parsed from their text, which <c>Convert.ChangeType</c> does not do.
/// </summary>
internal sealed partial class GridAdapterTemplate
{
    private void RenderHelpers()
    {
        const string json = "global::System.Text.Json";
        var filterable = model.Fields.Where(f => f.Filterable).Select(f => PragmaticNames.Literal(f.Json.ToLowerInvariant())).Distinct().ToList();
        AppendLine(filterable.Count == 0
            ? "    private static bool IsFilterable(string field) => false;"
            : $"    private static bool IsFilterable(string field) => field.ToLowerInvariant() is {string.Join(" or ", filterable)};");
        AppendLine();
        AppendLine(model.Withheld.Count == 0
            ? $"    private static global::System.Exception RejectedField(string field) => new {Adapters}.GridFieldRejectedException(field, {Adapters}.GridFieldRejection.Unknown);"
            : $"    private static global::System.Exception RejectedField(string field) => new {Adapters}.GridFieldRejectedException(field, field.ToLowerInvariant() is {string.Join(" or ", model.Withheld.Select(PragmaticNames.Literal))} ? {Adapters}.GridFieldRejection.Withheld : {Adapters}.GridFieldRejection.Unknown);");
        AppendLine();
        AppendLine($"    private static global::System.Exception Malformed() => new {Adapters}.GridFilterRejectedException(null, {Adapters}.GridFilterRejection.Malformed);");
        AppendLine();
        AppendLine($"    private static global::System.Exception Unusable(string field) => new {Adapters}.GridFilterRejectedException(field, {Adapters}.GridFilterRejection.UnusableValue);");
        AppendLine();
        AppendLine($"    private static global::System.Exception Unsupported(string field, string? op) => new {Adapters}.GridFilterRejectedException(field, {Adapters}.GridFilterRejection.UnsupportedOperator, op);");
        AppendLine();
        AppendLine("    private static int CountedTerms(int terms)");
        AppendLine($"        => terms > {Adapters}.GridFilterTooLargeException.MaxTerms ? throw new {Adapters}.GridFilterTooLargeException(terms) : terms;");
        AppendLine();
        AppendLine("    /// <summary>A clause or a group, as a list: a nested group that arrived as JSON is an array element, not a list.</summary>");
        AppendLine("    private static global::System.Collections.Generic.IReadOnlyList<object?>? Group(object? value) => value switch");
        AppendLine("    {");
        AppendLine($"        {json}.JsonElement {{ ValueKind: {json}.JsonValueKind.Array }} element => global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(element.EnumerateArray(), e => (object?)e)),");
        AppendLine("        global::System.Collections.Generic.IReadOnlyList<object?> list => list,");
        AppendLine("        _ => null,");
        AppendLine("    };");
        AppendLine();
        AppendLine("    private static global::System.Collections.Generic.IReadOnlyList<object?> GroupAt(global::System.Collections.Generic.IReadOnlyList<object?> filter, int index)");
        AppendLine("        => Group(filter[index]) ?? throw Malformed();");
        AppendLine();
        AppendLine("    /// <summary>What a grid sent as a CLR value: a JsonElement as the thing it holds, a number as a long when it is one; anything else unchanged.</summary>");
        AppendLine("    private static object? Unwrap(object? value)");
        AppendLine("    {");
        AppendLine($"        if (value is not {json}.JsonElement element)");
        AppendLine("            return value;");
        AppendLine("        return element.ValueKind switch");
        AppendLine("        {");
        AppendLine("            // A string whose bytes are not UTF-8 stays the element: no column converts it.");
        AppendLine($"            {json}.JsonValueKind.String => ReadString(element) ?? (object)element,");
        AppendLine($"            {json}.JsonValueKind.True => true,");
        AppendLine($"            {json}.JsonValueKind.False => false,");
        AppendLine($"            {json}.JsonValueKind.Null or {json}.JsonValueKind.Undefined => null,");
        AppendLine($"            {json}.JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole : element.GetDouble(),");
        AppendLine("            _ => value,");
        AppendLine("        };");
        AppendLine("    }");
        AppendLine();
        AppendLine("    /// <summary>A JsonElement's text, validated only when read: an unreadable string is a value the adapter cannot use, not an exception out of it.</summary>");
        AppendLine($"    private static string? ReadString({json}.JsonElement element)");
        AppendLine("    {");
        AppendLine("        try");
        AppendLine("        {");
        AppendLine("            return element.GetString();");
        AppendLine("        }");
        AppendLine("        catch (global::System.InvalidOperationException)");
        AppendLine("        {");
        AppendLine("            return null;");
        AppendLine("        }");
        AppendLine("    }");
        AppendLine();
        AppendLine("    /// <summary>The text a clause compares against, null when there is none: an array is not a text, nor a string that could not be read.</summary>");
        AppendLine("    private static string? Text(object? value)");
        AppendLine("    {");
        AppendLine($"        if (Unwrap(value) is not {json}.JsonElement element)");
        AppendLine("            return global::System.Convert.ToString(Unwrap(value), global::System.Globalization.CultureInfo.InvariantCulture);");
        AppendLine($"        if (element.ValueKind is {json}.JsonValueKind.String or {json}.JsonValueKind.Array)");
        AppendLine("            return null;");
        AppendLine("        try");
        AppendLine("        {");
        AppendLine("            return element.GetRawText();");
        AppendLine("        }");
        AppendLine("        catch (global::System.InvalidOperationException)");
        AppendLine("        {");
        AppendLine("            return null;");
        AppendLine("        }");
        AppendLine("    }");
        AppendLine();
        AppendLine("    private static bool TryConvert<T>(object? value, out T result) where T : struct, global::System.IConvertible");
        AppendLine("    {");
        AppendLine("        result = default;");
        AppendLine("        if (Unwrap(value) is not global::System.IConvertible convertible)");
        AppendLine("            return false;");
        AppendLine("        try");
        AppendLine("        {");
        AppendLine("            result = (T)convertible.ToType(typeof(T), global::System.Globalization.CultureInfo.InvariantCulture);");
        AppendLine("            return true;");
        AppendLine("        }");
        AppendLine("        catch (global::System.Exception e) when (e is global::System.FormatException or global::System.InvalidCastException or global::System.OverflowException)");
        AppendLine("        {");
        AppendLine("            return false;");
        AppendLine("        }");
        AppendLine("    }");
        AppendLine();
        AppendLine("    private static bool TryGuid(object? value, out global::System.Guid result)");
        AppendLine("    {");
        AppendLine("        if (Unwrap(value) is global::System.Guid guid)");
        AppendLine("        {");
        AppendLine("            result = guid;");
        AppendLine("            return true;");
        AppendLine("        }");
        AppendLine("        return global::System.Guid.TryParse(Text(value), out result);");
        AppendLine("    }");
        AppendLine();
        AppendLine("    private static bool TryDateTimeOffset(object? value, out global::System.DateTimeOffset result)");
        AppendLine("    {");
        AppendLine("        if (Unwrap(value) is global::System.DateTimeOffset instant)");
        AppendLine("        {");
        AppendLine("            result = instant;");
        AppendLine("            return true;");
        AppendLine("        }");
        AppendLine("        return global::System.DateTimeOffset.TryParse(Text(value), global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.AssumeUniversal, out result);");
        AppendLine("    }");
        AppendLine();
        AppendLine("    /// <summary>An enum by name (ignoring case) or by a defined value.</summary>");
        AppendLine("    private static bool TryEnum<T>(object? value, out T result) where T : struct, global::System.Enum");
        AppendLine("    {");
        AppendLine("        result = default;");
        AppendLine("        switch (Unwrap(value))");
        AppendLine("        {");
        AppendLine("            case string name:");
        AppendLine("                return global::System.Enum.TryParse(name, ignoreCase: true, out result);");
        AppendLine("            case long whole:");
        AppendLine("                result = (T)global::System.Enum.ToObject(typeof(T), whole);");
        AppendLine("                return global::System.Enum.IsDefined(result);");
        AppendLine("            default:");
        AppendLine("                return false;");
        AppendLine("        }");
        AppendLine("    }");
    }
}
