using ZLogger;

namespace Pragmatic.Logging.Benchmarks.Json.Probe;

/// <summary>A ZLogger processor that formats nothing: what a ZLogger call costs before any line is written.</summary>
internal sealed class ZLoggerEmptyProcessor : IAsyncLogProcessor
{
    public void Post(IZLoggerEntry log) => log.Return();

    public ValueTask DisposeAsync() => default;
}
