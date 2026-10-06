using System.Buffers;
using System.Text;
using ZLogger;

namespace Pragmatic.Logging.Benchmarks.Json;

/// <summary>
///     ZLogger's own JSON formatter, writing each entry into a reused buffer on the logging thread.
/// </summary>
/// <remarks>
///     ZLogger's stream processor formats on a background thread; this one formats inside the call, as
///     the other libraries' sinks do, so the row measures the work and not its hand-off.
/// </remarks>
internal sealed class ZLoggerJsonProcessor(IZLoggerFormatter formatter) : IAsyncLogProcessor
{
    private readonly ArrayBufferWriter<byte> _line = new(1024);

    /// <summary>The last line written; for the equivalence check only.</summary>
    public string LastLine => Encoding.UTF8.GetString(_line.WrittenSpan).TrimEnd('\r', '\n');

    public void Post(IZLoggerEntry log)
    {
        try
        {
            _line.ResetWrittenCount();
            formatter.FormatLogEntry(_line, log);
        }
        finally
        {
            log.Return();
        }
    }

    public ValueTask DisposeAsync() => default;
}
