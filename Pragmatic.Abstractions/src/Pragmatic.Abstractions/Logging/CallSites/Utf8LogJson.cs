using System.Buffers;
using System.Buffers.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Pragmatic.Logging.CallSites;

/// <summary>
///     The JSON of a value a generated writer describes, rendered once per call into a buffer the thread
///     keeps, for the three views a log call site has of an argument.
/// </summary>
/// <remarks>
///     <para>
///         Called by generated code, not meant to be called by hand. The writer passed in is generated for the
///         argument's type: it writes the members the type's JSON shape describes and the mask at the paths the
///         type declared <c>[NotLogged]</c> or <c>[PersonalData]</c>, so what this renders is what the declared
///         redactor would have produced by serializing the value and masking the copy.
///     </para>
///     <para>
///         ⚠️ The span <see cref="Render{T}" /> returns is the thread's buffer: it is valid until the next
///         render on the same thread. Every caller here consumes it before returning.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class Utf8LogJson
{
    [ThreadStatic] private static ArrayBufferWriter<byte>? t_buffer;
    [ThreadStatic] private static Utf8JsonWriter? t_writer;

    private static ReadOnlySpan<byte> Null => "(null)"u8;

    /// <summary>The value's JSON as UTF-8, in the thread's buffer.</summary>
    public static ReadOnlySpan<byte> Render<T>(T value, Action<Utf8JsonWriter, T> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var buffer = t_buffer ??= new ArrayBufferWriter<byte>(512);
        buffer.ResetWrittenCount();

        // Default options, so the escaping is System.Text.Json's default: the encoder the declared redactor
        // serializes with, and a line the same whichever path the value took.
        var writer = t_writer ??= new Utf8JsonWriter(buffer);
        writer.Reset(buffer);

        write(writer, value);
        writer.Flush();
        return buffer.WrittenSpan;
    }

    /// <summary>Appends the value's JSON to a message; <c>(null)</c> for a null.</summary>
    public static bool TryAppend<T>(Span<byte> destination, ref int written, T value, Action<Utf8JsonWriter, T> write)
    {
        var json = value is null ? Null : Render(value, write);
        if (!json.TryCopyTo(destination[written..]))
            return false;

        written += json.Length;
        return true;
    }

    /// <summary>Writes the value's JSON as a JSON string property; JSON null for a null.</summary>
    /// <remarks>
    ///     A string, not a nested object, because that is how the JSON providers write a complex property the
    ///     declared redactor has rendered: the two paths write the same line.
    /// </remarks>
    public static void Write<T>(Utf8JsonWriter writer, JsonEncodedText name, T value, Action<Utf8JsonWriter, T> write)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, Render(value, write));
    }

    /// <summary>The value's JSON as a <see cref="string" />, for the list view; null for a null.</summary>
    /// <remarks>
    ///     The list view is what a provider that knows nothing of Pragmatic reads, so it carries the masked
    ///     rendering rather than the object: a record's <c>ToString()</c> prints every member.
    /// </remarks>
    public static string? ToJsonString<T>(T value, Action<Utf8JsonWriter, T> write)
        => value is null ? null : Encoding.UTF8.GetString(Render(value, write));

    /// <summary>Writes a <see cref="DateTime" /> as ISO 8601 with its fraction trimmed, as System.Text.Json does.</summary>
    /// <remarks>
    ///     Formatted here and written as text, not through <c>WriteStringValue(DateTime)</c>: that overload
    ///     writes the characters unescaped, while the declared redactor's output passes through the default
    ///     encoder after masking, which escapes the <c>+</c> of an offset.
    /// </remarks>
    public static void WriteDateTime(Utf8JsonWriter writer, DateTime value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> buffer = stackalloc byte[40];
        value.TryFormat(buffer, out var count, "O", System.Globalization.CultureInfo.InvariantCulture);
        writer.WriteStringValue(buffer[..TrimFraction(buffer[..count])]);
    }

    /// <summary>Writes a <see cref="DateTimeOffset" /> as ISO 8601 with its fraction trimmed, as System.Text.Json does.</summary>
    /// <remarks>See <see cref="WriteDateTime" /> for why it is formatted here.</remarks>
    public static void WriteDateTimeOffset(Utf8JsonWriter writer, DateTimeOffset value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> buffer = stackalloc byte[40];
        value.TryFormat(buffer, out var count, "O", System.Globalization.CultureInfo.InvariantCulture);
        writer.WriteStringValue(buffer[..TrimFraction(buffer[..count])]);
    }

    /// <summary>
    ///     Trims the trailing zeros of the seven-digit fraction the <c>O</c> format writes, and the dot with
    ///     them when nothing is left: <c>…15.1200000+02:00</c> becomes <c>…15.12+02:00</c>, <c>…15.0000000Z</c>
    ///     becomes <c>…15Z</c>. The length of what remains, moved in place.
    /// </summary>
    private static int TrimFraction(Span<byte> text)
    {
        // yyyy-MM-ddTHH:mm:ss.fffffff, then the kind or the offset.
        const int Dot = 19;
        const int Digits = 7;
        if (text.Length < Dot + 1 + Digits || text[Dot] != (byte)'.')
            return text.Length;

        var kept = Digits;
        while (kept > 0 && text[Dot + kept] == (byte)'0')
            kept--;

        var start = kept == 0 ? Dot : Dot + 1 + kept;
        var tail = text[(Dot + 1 + Digits)..];
        tail.CopyTo(text[start..]);
        return start + tail.Length;
    }

    /// <summary>Writes a <see cref="TimeSpan" /> as System.Text.Json does: a string in the constant (<c>c</c>) format.</summary>
    public static void WriteTimeSpan(Utf8JsonWriter writer, TimeSpan value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> buffer = stackalloc byte[32];
        Utf8Formatter.TryFormat(value, buffer, out var count, 'c');
        writer.WriteStringValue(buffer[..count]);
    }

    /// <summary>Writes a <see cref="char" /> as System.Text.Json does: a one-character string.</summary>
    public static void WriteChar(Utf8JsonWriter writer, char value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
    }
}
