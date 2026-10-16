using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Benchmarks.Json.Probe;

/// <summary>
///     The JSON provider's classic path — a call that is not a generated call site — copied, with one switch per
///     thing it does: a row with a switch on, against the row with none, is what that thing costs.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ClassicJsonProbeVariant.Same" /> is <c>PragmaticJsonProvider.WriteJsonLogEntry</c> as it was
///         when #284 was opened, for a call without an exception or scopes: the benchmark's call. Its values are an
///         int, a string and a decimal, written as the provider writes them; any other type throws, so the probe
///         cannot quietly measure a different line.
///     </para>
///     <para>The setup holds every row that writes to the same bytes as the provider.</para>
/// </remarks>
internal sealed class ClassicJsonProbeProvider : PragmaticLoggerProviderBase
{
    private static readonly byte[] NewLine = Encoding.UTF8.GetBytes(Environment.NewLine);

    private readonly ClassicJsonProbeVariant _variant;
    private readonly Stream _output;
    private readonly TextWriter _textWriter;
    private readonly object _writeLock = new();
    private readonly JsonWriterOptions _options = new()
    {
        SkipValidation = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private ArrayBufferWriter<byte>? _line;
    private Utf8JsonWriter? _lineWriter;

    public ClassicJsonProbeProvider(IPragmaticProviderConfiguration configuration, Stream output, ClassicJsonProbeVariant variant)
        : base("ClassicProbe", configuration)
    {
        _variant = variant;
        _output = output;
        _textWriter = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
    }

    /// <summary>Lines written; the setup checks a call reaches this path.</summary>
    public int Lines { get; private set; }

    protected override void WriteLogCore(LogEvent logEvent)
    {
        lock (_writeLock)
        {
            Lines++;
            if (Has(ClassicJsonProbeVariant.EmptyWrite))
                return;

            if (Has(ClassicJsonProbeVariant.ReusedWriter))
            {
                var line = _line ??= new ArrayBufferWriter<byte>(1024);
                line.ResetWrittenCount();
                var writer = _lineWriter ??= new Utf8JsonWriter(line, _options);
                writer.Reset(line);
                Write(writer, logEvent);
                Output(line.WrittenSpan);
                return;
            }

            using var stream = new MemoryStream();
            using var streamWriter = new Utf8JsonWriter(stream, _options);
            Write(streamWriter, logEvent);
            Output(stream.ToArray());
        }
    }

    protected override void DisposeCore() => _textWriter.Flush();

    private void Write(Utf8JsonWriter writer, LogEvent logEvent)
    {
        writer.WriteStartObject();

        if (Has(ClassicJsonProbeVariant.StackTimestamp))
        {
            Span<byte> stamp = stackalloc byte[64];
            logEvent.Timestamp.TryFormat(stamp, out var written, Configuration.Formatting.TimestampFormat, CultureInfo.InvariantCulture);
            writer.WriteString("@timestamp", stamp[..written]);
        }
        else
        {
            writer.WriteString(
                "@timestamp", logEvent.Timestamp.ToString(Configuration.Formatting.TimestampFormat, CultureInfo.InvariantCulture));
        }

        writer.WriteString("@level", "INFO");
        writer.WriteString("@logger", logEvent.Category);
        writer.WriteString("@message", logEvent.Message);

        if (logEvent.EventId.Id != 0)
        {
            writer.WriteNumber("@eventId", logEvent.EventId.Id);
            if (!string.IsNullOrEmpty(logEvent.EventId.Name))
                writer.WriteString("@eventName", logEvent.EventId.Name);
        }

        if (!string.IsNullOrEmpty(logEvent.MessageTemplate))
            writer.WriteString("@messageTemplate", logEvent.MessageTemplate);

        if (logEvent.Properties.Count > 0)
        {
            writer.WriteStartObject("@properties");
            foreach (var property in logEvent.Properties)
            {
                writer.WritePropertyName(property.Key);
                switch (property.Value)
                {
                    case int number:
                        writer.WriteNumberValue(number);
                        break;
                    case decimal number:
                        writer.WriteNumberValue(number);
                        break;
                    case string text:
                        writer.WriteStringValue(text);
                        break;
                    default:
                        throw new InvalidOperationException($"The probe writes the benchmark's values only, not {property.Value}.");
                }
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.Flush();
    }

    private void Output(ReadOnlySpan<byte> json)
    {
        if (Has(ClassicJsonProbeVariant.BytesOut))
        {
            _output.Write(json);
            _output.Write(NewLine);
            _output.Flush();
            return;
        }

        _textWriter.WriteLine(Encoding.UTF8.GetString(json));
        _textWriter.Flush();
    }

    private bool Has(ClassicJsonProbeVariant variant) => (_variant & variant) != 0;
}
