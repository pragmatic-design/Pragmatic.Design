using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Pragmatic.Serialization;

namespace Pragmatic.Logging.CallSites;

/// <summary>
///     The values of a call site's <c>@properties</c> appended as JSON bytes: what a <c>Utf8JsonWriter</c> with
///     <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping" /> writes for them, without a writer call per value.
/// </summary>
/// <remarks>
///     <para>
///         Called by generated code, not meant to be called by hand. A string that encoder would escape is not
///         written here: the method returns false and sets <c>needsWriter</c>, and the caller writes the
///         properties through a writer, so the escaping is always the writer's own.
///     </para>
///     <para>
///         The encoder is the JSON provider's. A provider writing with another one does not use these bytes.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class Utf8LogJsonValues
{
    private static JavaScriptEncoder Encoder => JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>
    ///     The number <paramref name="value" /> writes as: integers in decimal digits, a decimal as its general
    ///     format, which is what <c>WriteNumberValue</c> writes for them.
    /// </summary>
    public static bool TryAppendJsonNumber<T>(Span<byte> destination, ref int written, T value)
        where T : IUtf8SpanFormattable
    {
        if (!value.TryFormat(destination[written..], out var count, default, CultureInfo.InvariantCulture))
            return false;

        written += count;
        return true;
    }

    /// <summary>The number, or <c>null</c>.</summary>
    public static bool TryAppendJsonNumber<T>(Span<byte> destination, ref int written, T? value)
        where T : struct, IUtf8SpanFormattable
        => value is { } present
            ? TryAppendJsonNumber(destination, ref written, present)
            : Utf8LogFormat.TryAppend(destination, ref written, "null"u8);

    /// <summary><c>true</c>, <c>false</c> or <c>null</c>.</summary>
    public static bool TryAppendJsonBoolean(Span<byte> destination, ref int written, bool? value)
        => Utf8LogFormat.TryAppend(destination, ref written, value switch
        {
            true => "true"u8,
            false => "false"u8,
            null => "null"u8,
        });

    /// <summary>The mask, as the string it is.</summary>
    public static bool TryAppendJsonMask(Span<byte> destination, ref int written)
        => Utf8LogFormat.TryAppend(destination, ref written, "\""u8)
           && Utf8LogFormat.TryAppend(destination, ref written, RedactionMask.Utf8)
           && Utf8LogFormat.TryAppend(destination, ref written, "\""u8);

    /// <summary>
    ///     The string between quotes, transcoded; <c>null</c> for a null. False with <paramref name="needsWriter" />
    ///     set when the writer would escape a character of it.
    /// </summary>
    public static bool TryAppendJsonString(Span<byte> destination, ref int written, string? value, ref bool needsWriter)
    {
        if (value is null)
            return Utf8LogFormat.TryAppend(destination, ref written, "null"u8);

        var start = written;
        if (!Utf8LogFormat.TryAppend(destination, ref written, "\""u8)
            || !Encoding.UTF8.TryGetBytes(value, destination[written..], out var count))
        {
            written = start;
            return false;
        }

        if (Encoder.FindFirstCharacterToEncodeUtf8(destination.Slice(written, count)) >= 0)
        {
            written = start;
            needsWriter = true;
            return false;
        }

        written += count;
        if (Utf8LogFormat.TryAppend(destination, ref written, "\""u8))
            return true;

        written = start;
        return false;
    }

    /// <summary>
    ///     The string from the UTF-8 bytes the message already rendered it to, between quotes; <c>null</c> for a
    ///     null, whatever the message rendered for it.
    /// </summary>
    public static bool TryAppendJsonString(
        Span<byte> destination, ref int written, string? value, ReadOnlySpan<byte> rendered, ref bool needsWriter)
    {
        if (value is null)
            return Utf8LogFormat.TryAppend(destination, ref written, "null"u8);

        if (Encoder.FindFirstCharacterToEncodeUtf8(rendered) >= 0)
        {
            needsWriter = true;
            return false;
        }

        var start = written;
        if (Utf8LogFormat.TryAppend(destination, ref written, "\""u8)
            && Utf8LogFormat.TryAppend(destination, ref written, rendered)
            && Utf8LogFormat.TryAppend(destination, ref written, "\""u8))
            return true;

        written = start;
        return false;
    }

    /// <summary>
    ///     Records <paramref name="value" /> in <paramref name="mark" />, inside the chain of appends a generated
    ///     message is: where a value's bytes start, and how long they are.
    /// </summary>
    public static bool Mark(int value, out int mark)
    {
        mark = value;
        return true;
    }
}
