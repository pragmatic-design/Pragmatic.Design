using System.Buffers.Text;
using BenchmarkDotNet.Attributes;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Benchmarks.Json.Probe;

/// <summary>
///     The timestamp of a JSON line alone: the provider's layout for its default format against the formatting
///     System.Text.Json and ZLogger use (<c>Utf8Formatter</c>, format <c>O</c>, another text). A row of nanoseconds,
///     steadier than the whole line it explains.
/// </summary>
[Config(typeof(LoggingBenchmarkConfig))]
[MemoryDiagnoser]
public class TimestampBenchmarks
{
    private readonly TimestampLayout _layout = TimestampLayout.Parse("yyyy-MM-ddTHH:mm:ss.fffZ")!;
    private readonly byte[] _buffer = new byte[64];
    private readonly DateTime _timestamp =new(2026, 10, 8, 21, 37, 5, 123, DateTimeKind.Utc);

    [Benchmark(Baseline = true)]
    public int Layout()
    {
        _layout.TryFormat(_timestamp, _buffer, out var written);
        return written;
    }

    [Benchmark]
    public int Utf8Formatter_O()
    {
        Utf8Formatter.TryFormat(_timestamp, _buffer, out var written, new System.Buffers.StandardFormat('O'));
        return written;
    }

    [Benchmark]
    public int DateTime_TryFormat_Custom()
    {
        _timestamp.TryFormat(_buffer, out var written, "yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        return written;
    }
}
