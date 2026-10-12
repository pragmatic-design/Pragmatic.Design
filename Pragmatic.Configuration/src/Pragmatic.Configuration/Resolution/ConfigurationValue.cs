using System.Globalization;

namespace Pragmatic.Configuration.Resolution;

/// <summary>
///     Converts a raw configuration value to a property's type, as <c>ConfigurationBinder</c> does, for the
///     typed binders the generator emits (<see cref="ITenantOptionsBinder{T}" />).
/// </summary>
/// <remarks>
///     <para>
///         Each method reproduces the <c>TypeConverter</c> the reflective binder uses for that type, with the
///         invariant culture: an integer may be written in hexadecimal (<c>#</c>, <c>0x</c>, <c>&amp;h</c>), a
///         date that is empty is the type's minimum, an enum is read by name or number without regard to case.
///         A value that does not convert throws the binder's <see cref="InvalidOperationException" />, naming
///         the key.
///     </para>
///     <para>
///         <c>TheGeneratedTenantBinderBindsAsTheConfigurationBinderTests</c> compares the two on the same
///         values, so a difference between them is a test failure rather than a tenant reading another value.
///     </para>
/// </remarks>
public static class ConfigurationValue
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static bool Boolean(string raw, string path) => Convert(raw, path, static s => bool.Parse(s.Trim()));

    public static byte Byte(string raw, string path)
        => Convert(raw, path, static s => Hex(s) is { } hex ? System.Convert.ToByte(hex, 16) : byte.Parse(s, NumberStyles.Integer, Invariant));

    public static sbyte SByte(string raw, string path)
        => Convert(raw, path, static s => Hex(s) is { } hex ? System.Convert.ToSByte(hex, 16) : sbyte.Parse(s, NumberStyles.Integer, Invariant));

    public static short Int16(string raw, string path)
        => Convert(raw, path, static s => Hex(s) is { } hex ? System.Convert.ToInt16(hex, 16) : short.Parse(s, NumberStyles.Integer, Invariant));

    public static ushort UInt16(string raw, string path)
        => Convert(raw, path, static s => Hex(s) is { } hex ? System.Convert.ToUInt16(hex, 16) : ushort.Parse(s, NumberStyles.Integer, Invariant));

    public static int Int32(string raw, string path)
        => Convert(raw, path, static s => Hex(s) is { } hex ? System.Convert.ToInt32(hex, 16) : int.Parse(s, NumberStyles.Integer, Invariant));

    public static uint UInt32(string raw, string path)
        => Convert(raw, path, static s => Hex(s) is { } hex ? System.Convert.ToUInt32(hex, 16) : uint.Parse(s, NumberStyles.Integer, Invariant));

    public static long Int64(string raw, string path)
        => Convert(raw, path, static s => Hex(s) is { } hex ? System.Convert.ToInt64(hex, 16) : long.Parse(s, NumberStyles.Integer, Invariant));

    public static ulong UInt64(string raw, string path)
        => Convert(raw, path, static s => Hex(s) is { } hex ? System.Convert.ToUInt64(hex, 16) : ulong.Parse(s, NumberStyles.Integer, Invariant));

    public static float Single(string raw, string path) => Convert(raw, path, static s => float.Parse(s.Trim(), NumberStyles.Float, Invariant));

    public static double Double(string raw, string path) => Convert(raw, path, static s => double.Parse(s.Trim(), NumberStyles.Float, Invariant));

    public static decimal Decimal(string raw, string path) => Convert(raw, path, static s => decimal.Parse(s.Trim(), NumberStyles.Float, Invariant));

    public static Guid Guid(string raw, string path) => Convert(raw, path, static s => new Guid(s.Trim()));

    public static TimeSpan TimeSpan(string raw, string path) => Convert(raw, path, static s => System.TimeSpan.Parse(s.Trim(), Invariant));

    public static DateTime DateTime(string raw, string path)
        => Convert(raw, path, static s => s.Trim() is { Length: > 0 } text ? System.DateTime.Parse(text, Invariant) : System.DateTime.MinValue);

    public static DateTimeOffset DateTimeOffset(string raw, string path)
        => Convert(raw, path, static s => s.Trim() is { Length: > 0 } text ? System.DateTimeOffset.Parse(text, Invariant) : System.DateTimeOffset.MinValue);

    public static TEnum Enum<TEnum>(string raw, string path)
        where TEnum : struct, Enum
        => Convert(raw, path, static s => System.Enum.TryParse<TEnum>(s, ignoreCase: true, out var value)
            ? value
            : throw new FormatException($"'{s}' is not a value of {typeof(TEnum).Name}."));

    private static T Convert<T>(string raw, string path, Func<string, T> parse)
    {
        try
        {
            return parse(raw);
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidOperationException($"Failed to convert configuration value '{raw}' at '{path}' to type '{typeof(T).FullName}'.", e);
        }
    }

    // The prefixes BaseNumberConverter reads as hexadecimal; it parses the rest as Convert.ToXxx(text, 16) does.
    private static string? Hex(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith('#'))
            return text[1..];
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || text.StartsWith("&h", StringComparison.OrdinalIgnoreCase))
            return text[2..];
        return null;
    }
}
