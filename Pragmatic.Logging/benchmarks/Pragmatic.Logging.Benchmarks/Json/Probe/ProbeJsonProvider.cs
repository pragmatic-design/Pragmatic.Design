using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.CallSites;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Benchmarks.Json.Probe;

/// <summary>
///     The JSON provider's UTF-8 path written field by field, with one switch per thing it does: a row with a
///     switch on, against the row with none, is what that thing costs.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ProbeVariant.Same" /> is the path with the timestamp read once and every field written by
///         the writer; <see cref="ProbeVariant.ParsedEachLine" /> is the path as it was before #109, and
///         <see cref="ProbeVariant.ConstantBlocks" /> the path the provider has now, which the
///         <c>RealProvider</c> row times. The setup holds every one of them to the same bytes.
///     </para>
///     <para>A change to <c>PragmaticJsonProvider.WriteUtf8State</c> is a change here too.</para>
/// </remarks>
internal sealed class ProbeJsonProvider : PragmaticLoggerProviderBase
{
    private static readonly JsonEncodedText TimestampProperty = JsonEncodedText.Encode("@timestamp");
    private static readonly JsonEncodedText LevelProperty = JsonEncodedText.Encode("@level");
    private static readonly JsonEncodedText LoggerProperty = JsonEncodedText.Encode("@logger");
    private static readonly JsonEncodedText MessageProperty = JsonEncodedText.Encode("@message");
    private static readonly JsonEncodedText EventIdProperty = JsonEncodedText.Encode("@eventId");
    private static readonly JsonEncodedText EventNameProperty = JsonEncodedText.Encode("@eventName");
    private static readonly JsonEncodedText TemplateProperty = JsonEncodedText.Encode("@messageTemplate");
    private static readonly JsonEncodedText PropertiesProperty = JsonEncodedText.Encode("@properties");
    private static readonly JsonEncodedText InformationLevel = JsonEncodedText.Encode("INFO");
    private static readonly byte[] NewLine = Encoding.UTF8.GetBytes(Environment.NewLine);

    private readonly ProbeVariant _variant;
    private readonly Stream _output;
    private readonly object _writeLock = new();
    private readonly JsonWriterOptions _options = new()
    {
        SkipValidation = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly bool _autoFlush;
    private ArrayBufferWriter<byte>? _line;
    private Utf8JsonWriter? _lineWriter;
    private byte[] _message = new byte[1024];
    private string? _timestampFormat;
    private TimestampLayout? _timestampLayout;
    private string? _categoryFor;
    private JsonEncodedText _category;
    private string? _eventNameFor;
    private JsonEncodedText _eventName;
    private string? _templateFor;
    private JsonEncodedText _template;
    private string? _levelAndLoggerFor;
    private LogLevel _levelAndLoggerLevel;
    private byte[]? _levelAndLogger;

    public ProbeJsonProvider(IPragmaticProviderConfiguration configuration, Stream output, ProbeVariant variant)
        : base("Probe", configuration)
    {
        _variant = variant;
        _output = output;
        _autoFlush = AutoFlush();
    }

    /// <summary>Lines written; the setup checks a call reaches this path rather than the classic one.</summary>
    public int Lines { get; private set; }

    protected internal override bool SupportsUtf8State => true;

    protected override void WriteLogCore(LogEntry logEntry) =>
        throw new InvalidOperationException("The probe measures the generated call site's path only.");

    protected override void WriteUtf8State<TState>(
        LogLevel logLevel, EventId eventId, IUtf8LogStateWriter<TState> utf8, in TState state, Exception? exception, string category)
    {
        if (Has(ProbeVariant.EmptyWrite))
        {
            Lines++;
            return;
        }

        var timestamp = DateTime.UtcNow;

        if (Has(ProbeVariant.NoLock))
        {
            Write(logLevel, eventId, utf8, in state, category, timestamp);
            return;
        }

        lock (_writeLock)
            Write(logLevel, eventId, utf8, in state, category, timestamp);
    }

    private void Write<TState>(
        LogLevel logLevel, EventId eventId, IUtf8LogStateWriter<TState> utf8, in TState state, string category, DateTime timestamp)
    {
        var message = Has(ProbeVariant.RawMessage) ? default : Message(utf8, in state);

        var line = _line ??= new ArrayBufferWriter<byte>(1024);
        line.ResetWrittenCount();
        var writer = _lineWriter ??= new Utf8JsonWriter(line, _options);
        writer.Reset(line);

        writer.WriteStartObject();
        if (Has(ProbeVariant.BuiltInTimestamp))
            writer.WriteString(TimestampProperty, timestamp);
        else
            WriteTimestamp(writer, timestamp);

        if (Has(ProbeVariant.ConstantBlocks))
        {
            if (Has(ProbeVariant.EventBlockOnly))
            {
                writer.WriteString(LevelProperty, logLevel == LogLevel.Information ? "INFO" : "UNKN");
                writer.WriteString(LoggerProperty, category);
            }
            else
            {
                writer.Flush();
                line.Write(LevelAndLogger(logLevel, category));
            }

            writer.WriteString(MessageProperty, message);
            writer.Flush();
            line.Write(EventBlock(eventId, utf8));
            WritePropertiesAndEnd(writer, utf8, in state);
            return;
        }

        if (Has(ProbeVariant.EncodedConstants))
        {
            writer.WriteString(LevelProperty, InformationLevel);
            writer.WriteString(LoggerProperty, Once(ref _categoryFor, ref _category, category));
        }
        else
        {
            writer.WriteString(LevelProperty, logLevel == LogLevel.Information ? "INFO" : "UNKN");
            writer.WriteString(LoggerProperty, category);
        }

        if (Has(ProbeVariant.RawMessage))
        {
            writer.WritePropertyName(MessageProperty);
            writer.WriteRawValue(QuotedMessage(utf8, in state), skipInputValidation: true);
        }
        else
        {
            writer.WriteString(MessageProperty, message);
        }

        if (!Has(ProbeVariant.NoExtraFields))
        {
            if (eventId.Id != 0)
            {
                writer.WriteNumber(EventIdProperty, eventId.Id);
                if (!string.IsNullOrEmpty(eventId.Name))
                {
                    if (Has(ProbeVariant.EncodedEventFields))
                        writer.WriteString(EventNameProperty, Once(ref _eventNameFor, ref _eventName, eventId.Name));
                    else
                        writer.WriteString(EventNameProperty, eventId.Name);
                }
            }

            if (!string.IsNullOrEmpty(utf8.Template) && !Has(ProbeVariant.NoTemplate))
            {
                if (Has(ProbeVariant.EncodedEventFields))
                    writer.WriteString(TemplateProperty, Once(ref _templateFor, ref _template, utf8.Template));
                else
                    writer.WriteString(TemplateProperty, utf8.Template);
            }
        }

        WritePropertiesAndEnd(writer, utf8, in state);
    }

    private void WritePropertiesAndEnd<TState>(Utf8JsonWriter writer, IUtf8LogStateWriter<TState> utf8, in TState state)
    {
        if (Configuration.IncludeStructuredProperties && utf8.PropertyCount > 0)
        {
            writer.WriteStartObject(PropertiesProperty);
            utf8.WriteProperties(in state, writer);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.Flush();
        Lines++;

        if (Has(ProbeVariant.NoStream))
            return;

        _output.Write(_line!.WrittenSpan);
        _output.Write(NewLine);
        if (Has(ProbeVariant.AutoFlushOnce) ? _autoFlush : AutoFlush())
            _output.Flush();
    }

    // ,"@level":"INFO","@logger":"<category>" — encoded once per logger and level.
    private byte[] LevelAndLogger(LogLevel logLevel, string category)
    {
        if (!ReferenceEquals(_levelAndLoggerFor, category) || _levelAndLoggerLevel != logLevel)
        {
            _levelAndLogger = BuildLevelAndLogger(logLevel, category);
            _levelAndLoggerFor = category;
            _levelAndLoggerLevel = logLevel;
        }

        return _levelAndLogger!;
    }

    // Apart from the method above: a lambda capturing its parameters there would allocate its closure on every
    // call, built or not.
    private byte[] BuildLevelAndLogger(LogLevel logLevel, string category) => Block(w =>
    {
        w.WriteString(LevelProperty, logLevel == LogLevel.Information ? "INFO" : "UNKN");
        w.WriteString(LoggerProperty, category);
    });

    // ,"@eventId":…,"@eventName":…,"@messageTemplate":… — encoded once per call site's state type.
    private byte[] EventBlock<TState>(EventId eventId, IUtf8LogStateWriter<TState> utf8)
    {
        if (ProbeEventBlock<TState>.Bytes is { } bytes
            && ProbeEventBlock<TState>.Id == eventId.Id
            && ReferenceEquals(ProbeEventBlock<TState>.Name, eventId.Name))
            return bytes;

        var block = BuildEventBlock(eventId, utf8.Template);
        ProbeEventBlock<TState>.Id = eventId.Id;
        ProbeEventBlock<TState>.Name = eventId.Name;
        ProbeEventBlock<TState>.Bytes = block;
        return block;
    }

    private byte[] BuildEventBlock(EventId eventId, string template) => Block(w =>
    {
        w.WriteNumber(EventIdProperty, eventId.Id);
        w.WriteString(EventNameProperty, eventId.Name);
        w.WriteString(TemplateProperty, template);
    });

    // The properties a writer writes inside an object, as the bytes that follow a property already written:
    // a leading comma, no braces. The same writer options, so the same escaping.
    /// <summary>Blocks encoded so far; after the first calls it must stop growing, or the cache is not one.</summary>
    public int BlocksBuilt { get; private set; }

    private byte[] Block(Action<Utf8JsonWriter> write)
    {
        BlocksBuilt++;
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, _options))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        var inner = buffer.WrittenSpan[1..^1];
        var block = new byte[inner.Length + 1];
        block[0] = (byte)',';
        inner.CopyTo(block.AsSpan(1));
        return block;
    }

    private bool Has(ProbeVariant variant) => (_variant & variant) != 0;

    private ReadOnlySpan<byte> Message<TState>(IUtf8LogStateWriter<TState> utf8, in TState state)
    {
        while (true)
        {
            if (utf8.TryFormatMessage(in state, _message, out var written))
                return _message.AsSpan(0, written);

            _message = new byte[_message.Length * 2];
        }
    }

    // The message rendered once, straight between the quotes of its JSON string.
    private ReadOnlySpan<byte> QuotedMessage<TState>(IUtf8LogStateWriter<TState> utf8, in TState state)
    {
        while (true)
        {
            if (utf8.TryFormatMessage(in state, _message.AsSpan(1, _message.Length - 2), out var written))
            {
                _message[0] = (byte)'"';
                _message[written + 1] = (byte)'"';
                return _message.AsSpan(0, written + 2);
            }

            _message = new byte[_message.Length * 2];
        }
    }

    private void WriteTimestamp(Utf8JsonWriter writer, DateTime timestamp)
    {
        var formatting = Configuration.Formatting;
        var effective = formatting.UseUtcTimestamp ? timestamp.ToUniversalTime() : timestamp.ToLocalTime();

        if (!Has(ProbeVariant.ParsedEachLine) && !ReferenceEquals(_timestampFormat, formatting.TimestampFormat))
        {
            _timestampLayout = TimestampLayout.Parse(formatting.TimestampFormat);
            _timestampFormat = formatting.TimestampFormat;
        }

        Span<byte> buffer = stackalloc byte[64];
        if (_timestampLayout is { } layout && layout.TryFormat(effective, buffer, out var laidOut))
            writer.WriteString(TimestampProperty, buffer[..laidOut]);
        else if (effective.TryFormat(buffer, out var written, formatting.TimestampFormat, CultureInfo.InvariantCulture))
            writer.WriteString(TimestampProperty, buffer[..written]);
        else
            writer.WriteString(TimestampProperty, effective.ToString(formatting.TimestampFormat, CultureInfo.InvariantCulture));
    }

    // What a generated call site would hold as a static field: the text encoded once, by the encoder the line uses.
    private JsonEncodedText Once(ref string? encodedFor, ref JsonEncodedText encoded, string text)
    {
        if (!ReferenceEquals(encodedFor, text))
        {
            encoded = JsonEncodedText.Encode(text, _options.Encoder);
            encodedFor = text;
        }

        return encoded;
    }

    private bool AutoFlush() =>
        !Configuration.CustomProperties.TryGetValue("AutoFlush", out var value) || value is not bool flush || flush;
}
