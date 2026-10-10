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

    /// <summary>
    ///     A value held by a member typed <c>object</c>: written by the serializer, under the options the response is
    ///     answered with, as it writes such a member — as whatever the value is at run time.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Through the options' own metadata for <see cref="object" />, the converter a member declared
    ///         <c>object</c> gets, so a converter the host registered for the runtime type applies here too.
    ///     </para>
    ///     <para>
    ///         ⚠️ One difference the writer cannot avoid: the serializer starts again at this value, so reference
    ///         handling and the depth limit count from here. A value that reaches an object the writer is still writing
    ///         around it is written once more where the host would write <c>null</c> for the cycle; a graph deeper than
    ///         the limit only from the root fits. Neither is in a shape the planner accepts on its own: both need the
    ///         <c>object</c> member to lead back into its own response.
    ///     </para>
    /// </remarks>
    public static void WriteUntyped(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        JsonSerializer.Serialize(writer, value, options.GetTypeInfo(typeof(object)));
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
