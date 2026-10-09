using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.CallSites;

namespace Pragmatic.Logging.Providers;

/// <summary>
///     A generated call site's line, written from its UTF-8 state into a reused buffer and straight to the
///     output: no entry, no message string, no intermediate stream.
/// </summary>
/// <remarks>
///     <para>
///         The line is the one <see cref="WriteLogCore" /> writes for the same call, field for field, and a
///         test compares the two. What differs is only the route: the classic path builds a
///         <see cref="LogEntry" />, renders the message to a <see cref="string" />, serializes into a
///         <see cref="MemoryStream" /> and decodes the bytes back into a string for a <see cref="TextWriter" />.
///     </para>
///     <para>
///         The buffer, the writer and the message buffer live with the provider and are used under the
///         write lock, so a call allocates nothing once they have grown to the size of its line.
///     </para>
/// </remarks>
public sealed partial class PragmaticJsonProvider
{
    private static readonly JsonEncodedText TimestampProperty = JsonEncodedText.Encode("@timestamp");
    private static readonly JsonEncodedText LevelProperty = JsonEncodedText.Encode("@level");
    private static readonly JsonEncodedText LoggerProperty = JsonEncodedText.Encode("@logger");
    private static readonly JsonEncodedText MessageProperty = JsonEncodedText.Encode("@message");
    private static readonly JsonEncodedText EventIdProperty = JsonEncodedText.Encode("@eventId");
    private static readonly JsonEncodedText EventNameProperty = JsonEncodedText.Encode("@eventName");
    private static readonly JsonEncodedText TemplateProperty = JsonEncodedText.Encode("@messageTemplate");
    private static readonly JsonEncodedText PropertiesProperty = JsonEncodedText.Encode("@properties");

    private static readonly byte[] NewLine = Encoding.UTF8.GetBytes(Environment.NewLine);
    private static ReadOnlySpan<byte> PropertiesOpening => ",\"@properties\":{"u8;

    private ArrayBufferWriter<byte>? _line;
    private Utf8JsonWriter? _lineWriter;
    private byte[] _message = new byte[1024];
    private byte[] _json = new byte[1024];
    private string? _timestampFormat;
    private TimestampLayout? _timestampLayout;

    // Set by the classic path when it leaves a line in the text writer's buffer.
    private bool _textPending;

    /// <inheritdoc />
    protected internal override bool SupportsUtf8State => true;

    /// <inheritdoc />
    protected override void WriteUtf8State<TState>(
        LogLevel logLevel, EventId eventId, IUtf8LogStateWriter<TState> utf8, in TState state, Exception? exception, string category)
    {
        var timestamp = DateTime.UtcNow;

        lock (_writeLock)
        {
            // A call site that writes its properties as JSON renders them with the message, each value once.
            ReadOnlySpan<byte> message;
            ReadOnlySpan<byte> properties = default;
            bool propertiesAsBytes;
            if (WritesBlocks
                && Configuration.IncludeStructuredProperties
                && utf8.PropertyCount > 0
                && utf8.WritesJsonProperties
                && MessageAndJson(utf8, in state, out message, out properties))
            {
                propertiesAsBytes = true;
            }
            else
            {
                propertiesAsBytes = false;
                message = Message(utf8, in state);
            }

            if (message.IsEmpty && exception is null)
                return;

            if (_filePath != null)
            {
                if (ShouldRollFile())
                    RollFile();
                EnsureFileCreated();
            }

            var line = _line ??= new ArrayBufferWriter<byte>(1024);
            line.ResetWrittenCount();

            var hasProperties = Configuration.IncludeStructuredProperties && utf8.PropertyCount > 0;
            if (WritesBlocks
                && exception is null
                && (propertiesAsBytes || !hasProperties)
                && TryWriteLine(line, logLevel, eventId, utf8, category, timestamp, message, hasProperties, properties))
            {
                WriteLine(line.WrittenSpan);
                return;
            }

            var writer = _lineWriter ??= new Utf8JsonWriter(line, _jsonOptions);
            writer.Reset(line);

            writer.WriteStartObject();
            WriteTimestamp(writer, timestamp);

            if (WritesBlocks && LevelAndLoggerBlock(logLevel, category) is { } levelAndLogger)
            {
                // The writer's bytes go to the line first; the block follows them, and the writer's next
                // property brings its own comma.
                writer.Flush();
                line.Write(levelAndLogger);
            }
            else
            {
                writer.WriteString(LevelProperty, GetLogLevelString(logLevel));
                writer.WriteString(LoggerProperty, category);
            }

            writer.WriteString(MessageProperty, message);

            // The exception sits between the event and the template, so with one the block cannot be used.
            if (WritesBlocks && exception is null)
            {
                writer.Flush();
                line.Write(EventBlock(eventId, utf8));
            }
            else
            {
                if (eventId.Id != 0)
                {
                    writer.WriteNumber(EventIdProperty, eventId.Id);
                    if (!string.IsNullOrEmpty(eventId.Name))
                        writer.WriteString(EventNameProperty, eventId.Name);
                }

                if (exception != null)
                    WriteExceptionDetails(writer, exception);

                if (!string.IsNullOrEmpty(utf8.Template))
                    writer.WriteString(TemplateProperty, utf8.Template);
            }

            if (propertiesAsBytes)
            {
                writer.Flush();
                line.Write(PropertiesOpening);
                line.Write(properties);
                line.Write("}"u8);
            }
            else if (Configuration.IncludeStructuredProperties && utf8.PropertyCount > 0)
            {
                writer.WriteStartObject(PropertiesProperty);
                utf8.WriteProperties(in state, writer);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.Flush();

            WriteLine(line.WrittenSpan);
        }
    }

    /// <summary>
    ///     The message and the properties' JSON, rendered together into the provider's buffers, which grow when they
    ///     do not fit. False when the call's properties are for the writer: a value the encoder would escape.
    /// </summary>
    private bool MessageAndJson<TState>(
        IUtf8LogStateWriter<TState> utf8, scoped in TState state, out ReadOnlySpan<byte> message, out ReadOnlySpan<byte> json)
    {
        while (true)
        {
            switch (utf8.TryFormatMessageAndJson(in state, _message, _json, out var messageWritten, out var jsonWritten))
            {
                case Utf8LogJsonStatus.Written:
                    message = _message.AsSpan(0, messageWritten);
                    json = _json.AsSpan(0, jsonWritten);
                    return true;

                case Utf8LogJsonStatus.BufferTooSmall:
                    _message = new byte[_message.Length * 2];
                    _json = new byte[_json.Length * 2];
                    continue;

                default:
                    message = default;
                    json = default;
                    return false;
            }
        }
    }

    /// <summary>The message rendered into the provider's buffer, which grows when a message does not fit.</summary>
    private ReadOnlySpan<byte> Message<TState>(IUtf8LogStateWriter<TState> utf8, scoped in TState state)
    {
        while (true)
        {
            if (utf8.TryFormatMessage(in state, _message, out var written))
                return _message.AsSpan(0, written);

            _message = new byte[_message.Length * 2];
        }
    }

    private DateTime EffectiveTimestamp(DateTime timestamp)
        => Configuration.Formatting.UseUtcTimestamp ? timestamp.ToUniversalTime() : timestamp.ToLocalTime();

    // The format is read once per format string, not on every line; the configuration can be replaced, so the
    // layout follows the string it was read from. Null for a format the layout does not read.
    private TimestampLayout? CurrentTimestampLayout()
    {
        var format = Configuration.Formatting.TimestampFormat;
        if (!ReferenceEquals(_timestampFormat, format))
        {
            _timestampLayout = TimestampLayout.Parse(format);
            _timestampFormat = format;
        }

        return _timestampLayout;
    }

    private void WriteTimestamp(Utf8JsonWriter writer, DateTime timestamp)
    {
        var formatting = Configuration.Formatting;
        var effective = EffectiveTimestamp(timestamp);

        Span<byte> buffer = stackalloc byte[64];
        if (CurrentTimestampLayout() is { } layout && layout.TryFormat(effective, buffer, out var laidOut))
            writer.WriteString(TimestampProperty, buffer[..laidOut]);
        else if (effective.TryFormat(buffer, out var written, formatting.TimestampFormat, CultureInfo.InvariantCulture))
            writer.WriteString(TimestampProperty, buffer[..written]);
        else
            writer.WriteString(TimestampProperty, effective.ToString(formatting.TimestampFormat, CultureInfo.InvariantCulture));
    }

    private void WriteLine(ReadOnlySpan<byte> json)
    {
        if (_filePath != null)
        {
            // The classic path writes through the stream writer and flushes per line; nothing is pending
            // there, but its encoder state is the same stream, so flush before going around it.
            _streamWriter!.Flush();
            _fileStream!.Write(json);
            _fileStream.Write(NewLine);
            _fileStream.Flush();
            _currentFileSize += json.Length + NewLine.Length;
            return;
        }

        if (_textPending)
        {
            _textWriter.Flush();
            _textPending = false;
        }

        _outputStream.Write(json);
        _outputStream.Write(NewLine);
        if (GetCustomProperty<bool>("AutoFlush", true))
            _outputStream.Flush();
    }
}
