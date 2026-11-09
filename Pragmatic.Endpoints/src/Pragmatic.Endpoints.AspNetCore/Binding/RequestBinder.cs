using System.Globalization;

namespace Pragmatic.Endpoints.Binding;

/// <summary>
///     Turns a raw request value into the type the endpoint declared, without reflection.
/// </summary>
/// <remarks>
///     <para>
///         Every overload is resolved at compile time by the generator, which knows each parameter's
///         type: <see cref="IParsable{TSelf}" /> for the numeric, date and <c>Guid</c> shapes, a
///         separate pair for enums (which are not <c>IParsable</c>), and a passthrough for strings.
///         Nothing here inspects a <c>Type</c> at runtime.
///     </para>
///     <para>
///         Culture is invariant on purpose. A route or query value is part of a URL, not of a user's
///         locale: <c>/orders/1.5</c> must mean the same number whichever machine parses it, and
///         ASP.NET's own binder makes the same choice.
///     </para>
/// </remarks>
public static class RequestBinder
{
    /// <summary>Binds a required value of a parsable type.</summary>
    /// <returns><c>false</c> when the value is absent or malformed — the caller answers 400.</returns>
    public static bool TryBind<T>(string? raw, out T value)
        where T : IParsable<T>
    {
        if (raw is null)
        {
            value = default!;
            return false;
        }

        return T.TryParse(raw, CultureInfo.InvariantCulture, out value!);
    }

    /// <summary>
    ///     Binds an optional value of a parsable value type. An absent value yields
    ///     <paramref name="fallback" />; a malformed one is still an error.
    /// </summary>
    public static bool TryBindOptional<T>(string? raw, T? fallback, out T? value)
        where T : struct, IParsable<T>
    {
        if (string.IsNullOrEmpty(raw))
        {
            value = fallback;
            return true;
        }

        if (T.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    ///     Binds a required enum, by name or by numeric value, case-insensitively — a value the enum defines, or
    ///     for a <c>[Flags]</c> enum a combination of the flags it defines.
    /// </summary>
    /// <returns><c>false</c> when the value is absent, malformed, or a number the enum does not define.</returns>
    public static bool TryBindEnum<T>(string? raw, out T value)
        where T : struct, Enum
    {
        if (raw is null)
        {
            value = default;
            return false;
        }

        return Enum.TryParse(raw, ignoreCase: true, out value) && IsDefinedValue(value);
    }

    /// <summary>Binds an optional enum. An absent value yields <paramref name="fallback" />.</summary>
    public static bool TryBindEnumOptional<T>(string? raw, T? fallback, out T? value)
        where T : struct, Enum
    {
        if (string.IsNullOrEmpty(raw))
        {
            value = fallback;
            return true;
        }

        if (Enum.TryParse<T>(raw, ignoreCase: true, out var parsed) && IsDefinedValue(parsed))
        {
            value = parsed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Whether <paramref name="value" /> is one the enum names: a member, or a combination of a <c>[Flags]</c> enum's.</summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <c>Enum.TryParse</c> alone accepts any integer, so <c>?status=99</c> reached the endpoint as a value no
    ///         member names, while an unknown name was refused: the same mistake, answered two ways.
    ///     </para>
    ///     <para>
    ///         Read through <c>ToString</c> rather than <c>Enum.IsDefined</c>, which knows nothing of <c>[Flags]</c> and
    ///         would refuse <c>Read | Write</c>. <c>ToString</c> honours the attribute itself — a defined value, or a
    ///         combination of defined flags, becomes names, and anything else becomes the number — and a C# member name
    ///         cannot start with a digit or a minus. So the one rule holds for both kinds of enum, without this code
    ///         reading the attribute.
    ///     </para>
    /// </remarks>
    private static bool IsDefinedValue<T>(T value)
        where T : struct, Enum
    {
        var text = value.ToString();
        return text.Length > 0 && !char.IsAsciiDigit(text[0]) && text[0] != '-';
    }

    /// <summary>Binds a required string. Present-but-empty counts as present.</summary>
    public static bool TryBindString(string? raw, out string value)
    {
        value = raw ?? string.Empty;
        return raw is not null;
    }

    /// <summary>
    ///     Binds a repeated string value. Its own method because <see cref="string" /> does not
    ///     implement <see cref="IParsable{TSelf}" /> and so cannot use the generic overload.
    /// </summary>
    public static bool TryBindManyStrings(string?[] raw, out string[] values)
    {
        var parsed = new string[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] is not { } item)
            {
                values = [];
                return false;
            }

            parsed[i] = item;
        }

        values = parsed;
        return true;
    }

    /// <summary>Binds every value of a parsable type, failing if any single one is malformed.</summary>
    public static bool TryBindMany<T>(string?[] raw, out T[] values)
        where T : IParsable<T>
    {
        var parsed = new T[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] is not { } item || !T.TryParse(item, CultureInfo.InvariantCulture, out parsed[i]!))
            {
                values = [];
                return false;
            }
        }

        values = parsed;
        return true;
    }
}
