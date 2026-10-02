using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Xunit;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
/// Regression test for the NDJSON dispose/flush path of <see cref="PragmaticEnhancedJsonProvider"/>.
/// <c>WriteLogsAtomicallyAsync</c> appends buffered lines without reading the destination file back:
/// a read-modify-rewrite over a path that already has an open append handle throws an IOException on
/// dispose and can leave the file empty. Appending lets the dispose-time flush persist all logs.
/// </summary>
public class PragmaticEnhancedJsonProviderNdjsonRegressionTests
{
    [Fact]
    public void Ndjson_PersistsAllBufferedLogs_OnDispose()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pragmatic-ndjson-regression-{Guid.NewGuid():N}.ndjson");

        try
        {
            // Async buffering + atomic writes is the path that hit the self-read IOException.
            // Async buffering requires batching to be enabled (ValidateConfiguration).
            var config = new PragmaticProviderConfiguration
            {
                MinimumLevel = LogLevel.Information,
                Performance = new PerformanceConfiguration { EnableBatching = true }
            };
            config.CustomProperties["EnableNDJSON"] = true;
            config.CustomProperties["EnableAsyncBuffering"] = true;
            config.CustomProperties["EnableAtomicWrites"] = true;

            using (var provider = new PragmaticEnhancedJsonProvider("ndjson-regression", config, path))
            {
                provider.WriteLog(MakeEntry("first entry"));
                provider.WriteLog(MakeEntry("second entry"));
                provider.WriteLog(MakeEntry("third entry"));
            } // Dispose drains the async buffer through WriteLogsAtomicallyAsync.

            File.Exists(path).Should().BeTrue();

            var content = File.ReadAllText(path);
            content.Should().NotBeNullOrWhiteSpace("the dispose-time flush must persist buffered logs");
            content.Should().Contain("first entry");
            content.Should().Contain("second entry");
            content.Should().Contain("third entry");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static LogEntry MakeEntry(string message) => new()
    {
        Timestamp = DateTime.UtcNow,
        LogLevel = LogLevel.Information,
        Category = "RegressionCategory",
        Message = message
    };
}
