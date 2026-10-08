using System.Buffers.Text;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Pragmatic.Serialization;

/// <summary>
///     The values System.Text.Json formats itself before it writes them, formatted the same way, for the writers the
///     generator emits.
/// </summary>
/// <remarks>
///     Called by generated code, not meant to be called by hand. Each method writes what the serializer's own
///     converter for that type writes; the generated writers' equivalence tests compare them with it.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class Utf8JsonValues
{
    /// <summary>A <see cref="TimeSpan" />: a string in the constant (<c>c</c>) format.</summary>
    public static void WriteTimeSpan(Utf8JsonWriter writer, TimeSpan value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> buffer = stackalloc byte[32];
        Utf8Formatter.TryFormat(value, buffer, out var count, 'c');
        writer.WriteStringValue(buffer[..count]);
    }

    /// <summary>A <see cref="char" />: a one-character string.</summary>
    public static void WriteChar(Utf8JsonWriter writer, char value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
    }

    /// <summary>A <see cref="DateOnly" />: <c>yyyy-MM-dd</c>.</summary>
    public static void WriteDateOnly(Utf8JsonWriter writer, DateOnly value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> buffer = stackalloc byte[16];
        value.TryFormat(buffer, out var count, "O", CultureInfo.InvariantCulture);
        writer.WriteStringValue(buffer[..count]);
    }

    /// <summary>A dictionary key that is a signed number: its invariant digits, as a property name.</summary>
    public static void WritePropertyName(Utf8JsonWriter writer, long key)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> buffer = stackalloc byte[20];
        Utf8Formatter.TryFormat(key, buffer, out var count);
        writer.WritePropertyName(buffer[..count]);
    }

    /// <summary>A dictionary key that is an unsigned number: its invariant digits, as a property name.</summary>
    public static void WritePropertyName(Utf8JsonWriter writer, ulong key)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> buffer = stackalloc byte[20];
        Utf8Formatter.TryFormat(key, buffer, out var count);
        writer.WritePropertyName(buffer[..count]);
    }

    /// <summary>A <see cref="TimeOnly" />: its time of day in the constant (<c>c</c>) format of a time span.</summary>
    public static void WriteTimeOnly(Utf8JsonWriter writer, TimeOnly value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> buffer = stackalloc byte[32];
        Utf8Formatter.TryFormat(value.ToTimeSpan(), buffer, out var count, 'c');
        writer.WriteStringValue(buffer[..count]);
    }
}
