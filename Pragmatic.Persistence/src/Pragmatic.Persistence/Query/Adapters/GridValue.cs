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
/// </remarks>
public static class GridValue
{
    /// <summary>
    ///     Converts a clause value to <typeparamref name="T" />, whether it arrived typed or as JSON.
    /// </summary>
    /// <typeparam name="T">The type the entity's property holds.</typeparam>
    /// <param name="value">The value as the clause carries it.</param>
    /// <returns>The converted value, or <c>default</c> when there is nothing usable.</returns>
    public static T? As<T>(object? value)
    {
        switch (value)
        {
            case null:
                return default;
            case T typed:
                return typed;
            case JsonElement json:
                return FromJson<T>(json);
        }

        // A number that arrived as a different numeric type, or a string standing in for a scalar:
        // IConvertible covers exactly the cases a query string or a loosely-typed adapter produces.
        try
        {
            return (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T),
                System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
        {
            return default;
        }
    }

    private static T? FromJson<T>(JsonElement json)
    {
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (json.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return default;

        object? converted = null;

        if (target == typeof(string))
            converted = json.ValueKind == JsonValueKind.String ? json.GetString() : json.ToString();
        else if (target == typeof(bool))
            converted = json.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(json.GetString(), out var b) ? b : null,
                _ => null,
            };
        else if (target == typeof(Guid))
            converted = json.TryGetGuid(out var guid) ? guid : null;
        else if (target == typeof(DateTimeOffset))
            converted = json.TryGetDateTimeOffset(out var dto) ? dto : null;
        else if (target == typeof(DateTime))
            converted = json.TryGetDateTime(out var dt) ? dt : null;
        else if (target == typeof(int))
            converted = json.TryGetInt32(out var i) ? i : null;
        else if (target == typeof(long))
            converted = json.TryGetInt64(out var l) ? l : null;
        else if (target == typeof(double))
            converted = json.TryGetDouble(out var d) ? d : null;
        else if (target == typeof(decimal))
            converted = json.TryGetDecimal(out var m) ? m : null;
        else if (target.IsEnum)
            // By name or by number, because a grid sends whichever its own model held. Enum.TryParse on
            // a known-at-compile-time enum type is not reflection over the entity.
            converted = json.ValueKind == JsonValueKind.String
                ? (Enum.TryParse(target, json.GetString(), ignoreCase: true, out var byName) ? byName : null)
                : (json.TryGetInt32(out var ordinal) && Enum.IsDefined(target, ordinal)
                    ? Enum.ToObject(target, ordinal)
                    : null);

        return converted is T result ? result : default;
    }
}
