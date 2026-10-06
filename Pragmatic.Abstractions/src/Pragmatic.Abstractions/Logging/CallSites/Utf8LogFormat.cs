using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Pragmatic.Serialization;

namespace Pragmatic.Logging.CallSites;

/// <summary>
///     The writing a generated log call site does, so that what it emits stays a list of calls.
/// </summary>
/// <remarks>
///     <para>
///         Called by generated code, not meant to be called by hand. The message methods append to a
///         destination span and advance <c>written</c>; each returns false, leaving <c>written</c> where
///         it was, when the span is too small, so the caller can retry with a larger one.
///     </para>
///     <para>
///         Values are rendered the way <c>Microsoft.Extensions.Logging</c> renders a message: invariant
///         culture, <c>(null)</c> for a null, <c>True</c>/<c>False</c> for a boolean, an enum by name. The
///         structured properties are written the way the Pragmatic JSON provider writes them, so a value
///         reads the same whichever path it took.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class Utf8LogFormat
{
    private static ReadOnlySpan<byte> Null => "(null)"u8;

    // ── Message ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Appends a literal part of the template.</summary>
    public static bool TryAppend(Span<byte> destination, ref int written, ReadOnlySpan<byte> literal)
    {
        if (!literal.TryCopyTo(destination[written..]))
            return false;

        written += literal.Length;
        return true;
    }

    /// <summary>Appends a string, transcoded; <c>(null)</c> for a null.</summary>
    public static bool TryAppend(Span<byte> destination, ref int written, string? value)
    {
        if (value is null)
            return TryAppend(destination, ref written, Null);

        if (!Encoding.UTF8.TryGetBytes(value, destination[written..], out var count))
            return false;

        written += count;
        return true;
    }

    /// <summary>Appends a boolean as <c>True</c> or <c>False</c>.</summary>
    public static bool TryAppend(Span<byte> destination, ref int written, bool value)
        => TryAppend(destination, ref written, value ? "True"u8 : "False"u8);

    /// <summary>Appends a nullable boolean; <c>(null)</c> for a null.</summary>
    public static bool TryAppend(Span<byte> destination, ref int written, bool? value)
        => value is { } present
            ? TryAppend(destination, ref written, present)
            : TryAppend(destination, ref written, Null);

    /// <summary>Appends a value that formats itself as UTF-8, in the invariant culture.</summary>
    public static bool TryAppendFormatted<T>(Span<byte> destination, ref int written, T value, ReadOnlySpan<char> format)
        where T : IUtf8SpanFormattable
    {
        if (!value.TryFormat(destination[written..], out var count, format, CultureInfo.InvariantCulture))
            return false;

        written += count;
        return true;
    }

    /// <summary>Appends a nullable value that formats itself as UTF-8; <c>(null)</c> for a null.</summary>
    public static bool TryAppendFormatted<T>(Span<byte> destination, ref int written, T? value, ReadOnlySpan<char> format)
        where T : struct, IUtf8SpanFormattable
        => value is { } present
            ? TryAppendFormatted(destination, ref written, present, format)
            : TryAppend(destination, ref written, Null);

    /// <summary>Appends an enum value by name, without boxing it.</summary>
    public static bool TryAppendEnum<TEnum>(Span<byte> destination, ref int written, TEnum value, ReadOnlySpan<char> format)
        where TEnum : struct, Enum
    {
        Span<char> name = stackalloc char[128];
        if (!Enum.TryFormat(value, name, out var chars, format))
            return TryAppend(destination, ref written, value.ToString());

        if (!Encoding.UTF8.TryGetBytes(name[..chars], destination[written..], out var count))
            return false;

        written += count;
        return true;
    }

    /// <summary>Appends a nullable enum value by name; <c>(null)</c> for a null.</summary>
    public static bool TryAppendEnum<TEnum>(Span<byte> destination, ref int written, TEnum? value, ReadOnlySpan<char> format)
        where TEnum : struct, Enum
        => value is { } present
            ? TryAppendEnum(destination, ref written, present, format)
            : TryAppend(destination, ref written, Null);

    /// <summary>Appends the mask an argument declared <c>[NotLogged]</c> or <c>[PersonalData]</c> is written as.</summary>
    public static bool TryAppendMask(Span<byte> destination, ref int written)
        => TryAppend(destination, ref written, RedactionMask.Utf8);

    /// <summary>
    ///     Appends a value the call site cannot write itself, through its <c>ToString()</c>.
    /// </summary>
    /// <remarks>
    ///     The path of an argument whose type is neither a string nor a formattable value. A call site
    ///     with one says so (<see cref="IUtf8LogState.IsSelfContained" /> is false), and a Pragmatic
    ///     provider then renders the entry from the list view, where declared redaction applies to it.
    /// </remarks>
    public static bool TryAppendObject(Span<byte> destination, ref int written, object? value)
        => value is IFormattable formattable
            ? TryAppend(destination, ref written, formattable.ToString(null, CultureInfo.InvariantCulture))
            : TryAppend(destination, ref written, value?.ToString());

    /// <summary>
    ///     The message of <paramref name="state" /> as a <see cref="string" />, for a provider that asks
    ///     for the formatter's output.
    /// </summary>
    public static string Render<TState>(in TState state) where TState : IUtf8LogState
    {
        Span<byte> buffer = stackalloc byte[512];
        if (state.TryFormatMessage(buffer, out var written))
            return Encoding.UTF8.GetString(buffer[..written]);

        // Bounded: a state that never fits is a defect in the generated code, not a message to grow for.
        for (var size = 2048; size <= MaxMessageBytes; size *= 2)
        {
            var rented = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                if (state.TryFormatMessage(rented, out written))
                    return Encoding.UTF8.GetString(rented, 0, written);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        throw new InvalidOperationException(
            $"The log message of {typeof(TState).Name} did not fit in {MaxMessageBytes} bytes.");
    }

    private const int MaxMessageBytes = 16 * 1024 * 1024;

    // ── Structured properties ───────────────────────────────────────────────────────────────────────

    /// <summary>Writes a value that formats itself as UTF-8 as a JSON string, in the invariant culture.</summary>
    public static void WriteFormatted<T>(Utf8JsonWriter writer, JsonEncodedText name, T value, ReadOnlySpan<char> format)
        where T : IUtf8SpanFormattable
    {
        Span<byte> buffer = stackalloc byte[128];
        if (value.TryFormat(buffer, out var count, format, CultureInfo.InvariantCulture))
        {
            writer.WriteString(name, buffer[..count]);
            return;
        }

        // Past 128 bytes: rare enough that a string is the right trade.
        writer.WriteString(name, value is IFormattable formattable
            ? formattable.ToString(format.IsEmpty ? null : format.ToString(), CultureInfo.InvariantCulture)
            : value.ToString());
    }

    /// <summary>Writes a nullable value that formats itself as UTF-8; JSON null for a null.</summary>
    public static void WriteFormatted<T>(Utf8JsonWriter writer, JsonEncodedText name, T? value, ReadOnlySpan<char> format)
        where T : struct, IUtf8SpanFormattable
    {
        if (value is { } present)
            WriteFormatted(writer, name, present, format);
        else
            writer.WriteNull(name);
    }

    /// <summary>Writes an enum value as its name, without boxing it.</summary>
    public static void WriteEnum<TEnum>(Utf8JsonWriter writer, JsonEncodedText name, TEnum value)
        where TEnum : struct, Enum
    {
        Span<char> buffer = stackalloc char[128];
        if (Enum.TryFormat(value, buffer, out var count))
            writer.WriteString(name, buffer[..count]);
        else
            writer.WriteString(name, value.ToString());
    }

    /// <summary>Writes a nullable enum value as its name; JSON null for a null.</summary>
    public static void WriteEnum<TEnum>(Utf8JsonWriter writer, JsonEncodedText name, TEnum? value)
        where TEnum : struct, Enum
    {
        if (value is { } present)
            WriteEnum(writer, name, present);
        else
            writer.WriteNull(name);
    }

    /// <summary>Writes the mask in place of an argument declared <c>[NotLogged]</c> or <c>[PersonalData]</c>.</summary>
    public static void WriteMask(Utf8JsonWriter writer, JsonEncodedText name)
        => writer.WriteString(name, RedactionMask.Utf8);
}
