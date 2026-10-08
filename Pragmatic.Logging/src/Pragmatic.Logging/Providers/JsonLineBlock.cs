using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
///     Parts of a JSON line that do not change between calls, encoded once into the bytes a writer would write
///     for them after a property already written: a leading comma, the properties, no braces.
/// </summary>
/// <remarks>
///     <para>
///         Each block is written by a <see cref="Utf8JsonWriter" /> with the line's own options, so its escaping
///         is the line's by construction. Copied into the line between two writes, it saves the writer a call,
///         an escaping pass and a copy per property: measured at 50 ns of a 259 ns line
///         (<c>Pragmatic.Logging/BENCHMARK-RESULTS.md</c>).
///     </para>
///     <para>
///         Only for options that do not indent: an indented line puts a line break and an indentation before
///         every property, which a block encoded once does not know.
///     </para>
/// </remarks>
internal static class JsonLineBlock
{
    /// <summary><c>,"first":"…","second":"…"</c>.</summary>
    public static byte[] Strings(
        JsonWriterOptions options, JsonEncodedText firstName, string first, JsonEncodedText secondName, string second)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = Start(buffer, options))
        {
            writer.WriteString(firstName, first);
            writer.WriteString(secondName, second);
            writer.WriteEndObject();
        }

        return Block(buffer);
    }

    /// <summary>
    ///     The event and the template, under the conditions the line writes them field by field: the id when it
    ///     is not zero, the name when there is an id and a name, the template when there is one.
    /// </summary>
    public static byte[] Event(
        JsonWriterOptions options, JsonEncodedText idName, JsonEncodedText nameName, JsonEncodedText templateName, EventId eventId, string? template)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = Start(buffer, options))
        {
            if (eventId.Id != 0)
            {
                writer.WriteNumber(idName, eventId.Id);
                if (!string.IsNullOrEmpty(eventId.Name))
                    writer.WriteString(nameName, eventId.Name);
            }

            if (!string.IsNullOrEmpty(template))
                writer.WriteString(templateName, template);

            writer.WriteEndObject();
        }

        return Block(buffer);
    }

    private static Utf8JsonWriter Start(ArrayBufferWriter<byte> buffer, JsonWriterOptions options)
    {
        var writer = new Utf8JsonWriter(buffer, options);
        writer.WriteStartObject();
        return writer;
    }

    // {…} → ,…  — or nothing at all for an empty object, so that no comma is written for no property.
    private static byte[] Block(ArrayBufferWriter<byte> buffer)
    {
        var inner = buffer.WrittenSpan[1..^1];
        if (inner.IsEmpty)
            return [];

        var block = new byte[inner.Length + 1];
        block[0] = (byte)',';
        inner.CopyTo(block.AsSpan(1));
        return block;
    }
}
