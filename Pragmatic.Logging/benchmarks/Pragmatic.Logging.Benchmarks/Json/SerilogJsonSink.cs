using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace Pragmatic.Logging.Benchmarks.Json;

/// <summary>Serilog's own JSON formatter, writing each event into a reused writer.</summary>
internal sealed class SerilogJsonSink : ILogEventSink
{
    private readonly JsonFormatter _formatter = new(closingDelimiter: "\n", renderMessage: true);
    private readonly StringWriter _line = new();

    /// <summary>The last line written; for the equivalence check only.</summary>
    public string LastLine => _line.ToString().TrimEnd('\n');

    public void Emit(LogEvent logEvent)
    {
        _line.GetStringBuilder().Clear();
        _formatter.Format(logEvent, _line);
    }
}
