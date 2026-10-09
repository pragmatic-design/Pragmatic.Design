using System.Buffers;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.CallSites;

namespace Pragmatic.Logging.Providers;

/// <summary>
///     A generated call site's line written as bytes, without a <see cref="System.Text.Json.Utf8JsonWriter" />, when
///     nothing in it needs the writer: the same line, byte for byte.
/// </summary>
/// <remarks>
///     <para>
///         Every part is one whose bytes are known to be the writer's: the timestamp from a layout of digits and
///         separators, the level and logger and the event as blocks the writer encoded once, the message when the
///         line's encoder would escape nothing in it, the properties as the call site wrote them. With the
///         properties as bytes, it took the line from 259 to 158 ns (<c>Pragmatic.Logging/BENCHMARK-RESULTS.md</c>).
///     </para>
///     <para>
///         Anything else — an exception, a message to escape, properties for the writer, a timestamp format the
///         layout does not read, an indented line — goes through the writer, as before.
///     </para>
/// </remarks>
public sealed partial class PragmaticJsonProvider
{
    private static ReadOnlySpan<byte> LineOpening => "{\"@timestamp\":\""u8;
    private static ReadOnlySpan<byte> MessageOpening => ",\"@message\":\""u8;

    // Under the write lock, as every caller is. False, with nothing written, when a part needs the writer.
    private bool TryWriteLine<TState>(
        ArrayBufferWriter<byte> line,
        LogLevel logLevel,
        EventId eventId,
        IUtf8LogStateWriter<TState> utf8,
        string category,
        DateTime timestamp,
        ReadOnlySpan<byte> message,
        bool hasProperties,
        ReadOnlySpan<byte> properties)
    {
        if (CurrentTimestampLayout() is not { } layout
            || LevelAndLoggerBlock(logLevel, category) is not { } levelAndLogger
            || _jsonOptions.Encoder!.FindFirstCharacterToEncodeUtf8(message) >= 0)
            return false;

        Span<byte> stamp = stackalloc byte[64];
        if (!layout.TryFormat(EffectiveTimestamp(timestamp), stamp, out var stampLength))
            return false;

        var eventBlock = EventBlock(eventId, utf8);
        var length = LineOpening.Length + stampLength + 1
                     + levelAndLogger.Length
                     + MessageOpening.Length + message.Length + 1
                     + eventBlock.Length
                     + (hasProperties ? PropertiesOpening.Length + properties.Length + 1 : 0)
                     + 1;

        var destination = line.GetSpan(length);
        var written = 0;
        Append(destination, ref written, LineOpening);
        Append(destination, ref written, stamp[..stampLength]);
        destination[written++] = (byte)'"';
        Append(destination, ref written, levelAndLogger);
        Append(destination, ref written, MessageOpening);
        Append(destination, ref written, message);
        destination[written++] = (byte)'"';
        Append(destination, ref written, eventBlock);
        if (hasProperties)
        {
            Append(destination, ref written, PropertiesOpening);
            Append(destination, ref written, properties);
            destination[written++] = (byte)'}';
        }

        destination[written++] = (byte)'}';
        line.Advance(written);
        return true;
    }

    private static void Append(Span<byte> destination, ref int written, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(destination[written..]);
        written += bytes.Length;
    }
}
