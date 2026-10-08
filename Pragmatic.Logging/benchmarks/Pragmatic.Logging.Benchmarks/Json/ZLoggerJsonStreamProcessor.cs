using System.Buffers;
using System.Text;
using ZLogger;

namespace Pragmatic.Logging.Benchmarks.Json;

/// <summary>
///     ZLogger's JSON formatter with the sink Pragmatic's provider has: the line formatted on the logging
///     thread, then written to a stream under a lock, a line break after it, and a flush per line.
/// </summary>
/// <remarks>
///     <see cref="ZLoggerJsonProcessor" /> keeps the line in a buffer, which Pragmatic's provider cannot do; this
///     one pays what the provider pays for the stream, so the difference between the two rows is the library's
///     and not the sink's.
/// </remarks>
internal sealed class ZLoggerJsonStreamProcessor(IZLoggerFormatter formatter, Stream output) : IAsyncLogProcessor
{
    private static readonly byte[] NewLine = Encoding.UTF8.GetBytes(Environment.NewLine);

    private readonly ArrayBufferWriter<byte> _line = new(1024);
    private readonly Lock _gate = new();

    public void Post(IZLoggerEntry log)
    {
        try
        {
            lock (_gate)
            {
                _line.ResetWrittenCount();
                formatter.FormatLogEntry(_line, log);
                output.Write(_line.WrittenSpan);
                output.Write(NewLine);
                output.Flush();
            }
        }
        finally
        {
            log.Return();
        }
    }

    public ValueTask DisposeAsync() => default;
}
