using System.Collections;
using System.Text.Json;

namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     A filter value as a grid client sent it, read into the CLR value a column compares with. Shared by the runtime
///     adapters, whose request models carry the value as <c>object?</c>: bound from JSON, that is a
///     <see cref="JsonElement" />, which neither <see cref="Convert.ChangeType(object, Type)" /> nor a type test reads.
/// </summary>
internal static class FilterValue
{
    /// <summary>
    ///     The value converted to <paramref name="targetType" />, unwrapping a <see cref="JsonElement" /> first.
    /// </summary>
    /// <exception cref="FormatException">The text is not a value of the type.</exception>
    /// <exception cref="InvalidCastException">The value cannot become one of the type.</exception>
    /// <exception cref="OverflowException">The value is past the type's range.</exception>
    /// <exception cref="ArgumentException">The text names no member of the enum.</exception>
    public static object? Coerce(object? value, Type targetType)
    {
        value = Unwrap(value);
        if (value == null)
            return null;

        // Already the correct type
        if (targetType.IsInstanceOfType(value))
            return value;

        // Handle Nullable<T> — convert to the underlying type
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Enums need Enum.Parse for strings and Enum.ToObject for numeric values;
        // Convert.ChangeType throws InvalidCastException for both.
        if (underlyingType.IsEnum)
        {
            if (value is string s)
                return Enum.Parse(underlyingType, s, ignoreCase: true);
            return Enum.ToObject(underlyingType, value);
        }

        return Convert.ChangeType(value, underlyingType);
    }

    /// <summary>
    ///     The elements of a list value, a JSON array or a CLR collection; null for anything else. A string is not a
    ///     list of its characters.
    /// </summary>
    public static IReadOnlyList<object?>? Elements(object? value) => Unwrap(value) switch
    {
        string => null,
        IReadOnlyList<object?> list => list,
        IEnumerable items => [.. items.Cast<object?>()],
        _ => null,
    };

    /// <summary>The CLR value of a <see cref="JsonElement" />; any other value as it is.</summary>
    public static object? Unwrap(object? value) => value is JsonElement json ? Read(json) : value;

    /// <summary>
    ///     Converts a <see cref="JsonElement" /> to the most appropriate CLR type.
    /// </summary>
    public static object? Read(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => ReadString(element),
            // (object) on both arms: without it the ternary's type is double, every integer arrived as one, and
            // an enum by number (Enum.ToObject refuses a double) or a long past 2^53 could not be filtered on.
            JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(e => (object?)Read(e)).ToList() as object,
            _ => ReadRawText(element)
        };
    }

    /// <summary>The text of a JSON string, or <c>null</c> when the bytes the client sent are not UTF-8.</summary>
    /// <remarks>
    ///     A filter value binds as a <see cref="JsonElement" />, and its text is not validated until it is read:
    ///     <see cref="JsonElement.GetString" /> then throws <see cref="InvalidOperationException" />, which escaped the
    ///     adapter as a 500 (found by the fuzz harness, #310). An unreadable string is a value the adapter cannot use,
    ///     like one of the wrong type.
    /// </remarks>
    public static string? ReadString(JsonElement element)
    {
        try
        {
            return element.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The raw text of an object, or <c>null</c> when it holds bytes that are not UTF-8.</summary>
    /// <remarks>
    ///     <see cref="JsonElement.GetRawText" /> transcodes like <see cref="JsonElement.GetString" />, and threw the same
    ///     way on a key the client spelled in such bytes (found by the fuzz harness, #310).
    /// </remarks>
    public static string? ReadRawText(JsonElement element)
    {
        try
        {
            return element.GetRawText();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
