using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     Reads a <see cref="FilterClause.Value" /> as the type the field actually holds.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>This exists so the canonical request survives its own round trip.</b>
///         <c>FilterClause.Value</c> is an <c>object?</c> so that any grid's payload fits in it; when
///         that payload arrives as JSON, System.Text.Json fills it with a <see cref="JsonElement" />.
///         A straight cast to the field's CLR type — <c>(string)value</c> — throws
///         <c>InvalidCastException</c> inside the LINQ expression and answers <b>500</b>.
///         A public, init-only, serialisable type that cannot be deserialised into something usable is
///         a trap, and this is the conversion that removes it.
///     </para>
///     <para>
///         No reflection and no <c>Deserialize&lt;T&gt;</c>: every branch is a typed comparison against
///         <c>typeof(T)</c> and a direct <c>JsonElement</c> accessor, so the whole thing is AOT-safe and
///         adds nothing to the reflection budget.
///     </para>
///     <para>
///         ⚠️ <b>No value and an unusable value are two answers (#325).</b> <see cref="As{T}" /> gives
///         <c>default</c> for both, and the bridge dropped the clause either way: a filter left out answers
///         with more rows than the client asked for. <see cref="TryAs{T}" /> tells them apart.
///     </para>
/// </remarks>
public static class GridValue
{
    /// <summary>
    ///     Converts a clause value to <typeparamref name="T" />, whether it arrived typed or as JSON.
    /// </summary>
    /// <typeparam name="T">The type the entity's property holds.</typeparam>
    /// <param name="value">The value as the clause carries it.</param>
    /// <returns>The converted value, or <c>default</c> when there is nothing usable.</returns>
    public static T? As<T>(object? value) => TryAs<T>(value, out var result) ? result : default;

    /// <summary>
    ///     Converts a clause value to <typeparamref name="T" />, and says whether it could.
    /// </summary>
    /// <typeparam name="T">The type the entity's property holds.</typeparam>
    /// <param name="value">The value as the clause carries it.</param>
    /// <param name="result">The converted value; <c>default</c> when there is no value, or when it cannot be read.</param>
    /// <returns>
    ///     <c>true</c> when the value converts, or when there is none (<see cref="IsNull" />); <c>false</c> when there is
    ///     one and it is not a <typeparamref name="T" />: another type, past its range, a list, or a string whose bytes
    ///     are not UTF-8.
    /// </returns>
    public static bool TryAs<T>(object? value, out T? result)
    {
        result = default;
        switch (value)
        {
            case null:
                return true;
            case T typed:
                result = typed;
                return true;
            case JsonElement json:
                return TryFromJson(json, out result);
        }

        // A number that arrived as a different numeric type, or a string standing in for a scalar:
        // IConvertible covers exactly the cases a query string or a loosely-typed adapter produces.
        try
        {
            result = (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>Whether a clause carries no value: <c>null</c>, or a JSON <c>null</c>.</summary>
    public static bool IsNull(object? value)
        => value is null or JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined };

    /// <summary>
    ///     The values of an <c>In</c> or a <c>Between</c>: a JSON array or a CLR collection, each element as it is; null
    ///     for anything else. A string is not a list of its characters.
    /// </summary>
    public static IReadOnlyList<object?>? Elements(object? value) => value switch
    {
        string => null,
        JsonElement { ValueKind: JsonValueKind.Array } json => [.. json.EnumerateArray().Select(e => (object?)e)],
        IEnumerable items => [.. items.Cast<object?>()],
        _ => null,
    };

    private static bool TryFromJson<T>(JsonElement json, out T? result)
    {
        result = default;
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (json.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;

        object? converted;
        if (target == typeof(string))
            converted = json.ValueKind switch
            {
                JsonValueKind.String => Text(json),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => json.GetRawText(),
                _ => null,
            };
        else if (target == typeof(bool))
            converted = json.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(Text(json), out var b) ? b : null,
                _ => null,
            };
        else if (target.IsEnum)
            // By name or by number, because a grid sends whichever its own model held. Enum.TryParse on
            // a known-at-compile-time enum type is not reflection over the entity.
            converted = json.ValueKind switch
            {
                JsonValueKind.String => Enum.TryParse(target, Text(json), ignoreCase: true, out var byName) ? byName : null,
                JsonValueKind.Number => json.TryGetInt32(out var ordinal) && Enum.IsDefined(target, ordinal)
                    ? Enum.ToObject(target, ordinal)
                    : null,
                _ => null,
            };
        else if (json.ValueKind == JsonValueKind.Number)
            converted = FromNumber(json, target);
        else if (json.ValueKind == JsonValueKind.String && (target == typeof(Guid) || target == typeof(DateTime) || target == typeof(DateTimeOffset)))
            // The JSON accessors, which read the ISO forms a serializer writes and nothing looser.
            converted = FromJsonString(json, target);
        else if (json.ValueKind == JsonValueKind.String)
            // A string for a scalar: a date, a GUID, or a number a client sent quoted. Read with the invariant
            // culture, as a grid's wire format has no other.
            converted = Text(json) is { } text ? FromText(text, target) : null;
        else
            converted = null;

        if (converted is not T typed)
            return false;

        result = typed;
        return true;
    }

    // The JsonElement accessors for numbers; each TryGet throws on any other kind, so only a number gets here.
    private static object? FromNumber(JsonElement json, Type target)
    {
        if (target == typeof(int)) return json.TryGetInt32(out var i) ? i : null;
        if (target == typeof(long)) return json.TryGetInt64(out var l) ? l : null;
        if (target == typeof(short)) return json.TryGetInt16(out var s) ? s : null;
        if (target == typeof(byte)) return json.TryGetByte(out var b) ? b : null;
        if (target == typeof(decimal)) return json.TryGetDecimal(out var m) ? m : null;
        if (target == typeof(double)) return json.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
        if (target == typeof(float)) return json.TryGetSingle(out var f) && float.IsFinite(f) ? f : null;
        return null;
    }

    // TryGetGuid and the date accessors read the string's text, and throw like GetString on bytes that are not UTF-8.
    private static object? FromJsonString(JsonElement json, Type target)
    {
        try
        {
            if (target == typeof(Guid)) return json.TryGetGuid(out var g) ? g : null;
            if (target == typeof(DateTimeOffset)) return json.TryGetDateTimeOffset(out var dto) ? dto : null;
            return json.TryGetDateTime(out var dt) ? dt : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static object? FromText(string text, Type target)
    {
        var invariant = CultureInfo.InvariantCulture;
        if (target == typeof(DateOnly)) return DateOnly.TryParse(text, invariant, out var date) ? date : null;
        if (target == typeof(TimeOnly)) return TimeOnly.TryParse(text, invariant, out var time) ? time : null;
        if (target == typeof(TimeSpan)) return TimeSpan.TryParse(text, invariant, out var span) ? span : null;
        if (target == typeof(int)) return int.TryParse(text, NumberStyles.Integer, invariant, out var i) ? i : null;
        if (target == typeof(long)) return long.TryParse(text, NumberStyles.Integer, invariant, out var l) ? l : null;
        if (target == typeof(short)) return short.TryParse(text, NumberStyles.Integer, invariant, out var s) ? s : null;
        if (target == typeof(byte)) return byte.TryParse(text, NumberStyles.Integer, invariant, out var b) ? b : null;
        if (target == typeof(decimal)) return decimal.TryParse(text, NumberStyles.Number, invariant, out var m) ? m : null;
        if (target == typeof(double)) return double.TryParse(text, NumberStyles.Float, invariant, out var d) && double.IsFinite(d) ? d : null;
        if (target == typeof(float)) return float.TryParse(text, NumberStyles.Float, invariant, out var f) && float.IsFinite(f) ? f : null;
        return null;
    }

    // A JSON string's text is validated only when it is read, and GetString throws on bytes that are not UTF-8: such a
    // string is a value no column can be compared with.
    private static string? Text(JsonElement json)
    {
        try
        {
            return json.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
