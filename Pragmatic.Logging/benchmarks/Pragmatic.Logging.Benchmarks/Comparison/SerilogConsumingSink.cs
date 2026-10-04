using System.Globalization;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Pragmatic.Logging.Benchmarks.Comparison;

/// <summary>A Serilog sink that consumes every event it is given; see <see cref="EventConsumer" />.</summary>
/// <remarks>
///     The message is rendered with <c>{Message:l}</c>: Serilog quotes string values by default, and the
///     other three libraries do not, so the default would fail the equivalence check on formatting alone.
///     Not <c>:lj</c>, which writes a date as JSON where the others write it as text.
/// </remarks>
internal sealed class SerilogConsumingSink : ILogEventSink
{
    private static readonly MessageTemplateTextFormatter MessageOnly = new("{Message:l}", CultureInfo.InvariantCulture);

    private readonly EventConsumer _consumer;
    private readonly StringWriter _writer;

    public SerilogConsumingSink(EventConsumer consumer)
    {
        _consumer = consumer;
        _writer = new StringWriter(consumer.Buffer, CultureInfo.InvariantCulture);
    }

    public void Emit(LogEvent logEvent)
    {
        _consumer.Begin();
        MessageOnly.Format(logEvent, _writer);
        _consumer.EndMessage();

        foreach (var property in logEvent.Properties)
        {
            if (!EventConsumer.IsLibraryMetadata(property.Key))
                _consumer.Property(property.Key, property.Value is ScalarValue scalar ? scalar.Value : property.Value);
        }

        _consumer.Exception(logEvent.Exception);
        _consumer.Complete();
    }
}
