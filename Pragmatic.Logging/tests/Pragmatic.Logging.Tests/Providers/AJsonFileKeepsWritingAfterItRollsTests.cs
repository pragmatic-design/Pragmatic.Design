using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
///     A JSON file rolls when it reaches its size or a new day begins: the old lines move to the rolled file, and
///     the lines after the roll are written to a new file at the configured path.
/// </summary>
public sealed class AJsonFileKeepsWritingAfterItRollsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pragmatic-roll-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(_directory, "log.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void AfterASizeRoll_TheNextLineIsInTheNewFile()
    {
        var configuration = Configuration();
        configuration.CustomProperties["RollByDate"] = false;
        configuration.CustomProperties["MaxFileSizeBytes"] = 1L;

        using (var provider = new PragmaticJsonProvider("json", configuration, File))
        {
            var logger = provider.CreateLogger("Orders");
            logger.LogInformation("first");
            logger.LogInformation("second");
        }

        System.IO.File.ReadAllText(File).Should().Contain("\"@message\":\"second\"").And.NotContain("\"@message\":\"first\"");
        RolledFiles().Should().HaveCount(1);
        System.IO.File.ReadAllText(RolledFiles()[0]).Should().Contain("\"@message\":\"first\"");
    }

    [Fact]
    public void AfterADateRoll_TheNextLineIsInTheNewFile()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 16, 23, 59, 0, TimeSpan.Zero));

        using (var provider = new PragmaticJsonProvider("json", Configuration(), File) { Clock = clock })
        {
            var logger = provider.CreateLogger("Orders");
            logger.LogInformation("first");
            clock.Now = clock.Now.AddMinutes(2);
            logger.LogInformation("second");
        }

        System.IO.File.ReadAllText(File).Should().Contain("\"@message\":\"second\"").And.NotContain("\"@message\":\"first\"");
        var rolled = Path.Combine(_directory, "log_20261017-000100.json");
        System.IO.File.ReadAllText(rolled).Should().Contain("\"@message\":\"first\"");
    }

    /// <summary>The enhanced provider's synchronous write rolls through the same method as its batches.</summary>
    [Fact]
    public void TheEnhancedProvider_AfterASizeRoll_TheNextLineIsInTheNewFile()
    {
        var configuration = Configuration();
        configuration.CustomProperties["RollByDate"] = false;
        configuration.CustomProperties["MaxFileSizeBytes"] = 1L;
        configuration.CustomProperties["EnableAsyncBuffering"] = false;
        configuration.CustomProperties["RollStrategy"] = "sequential";

        using (var provider = new PragmaticEnhancedJsonProvider("json", configuration, File))
        {
            var logger = provider.CreateLogger("Orders");
            logger.LogInformation("first");
            logger.LogInformation("second");
        }

        System.IO.File.ReadAllText(File).Should().Contain("second").And.NotContain("first");
        System.IO.File.ReadAllText(Path.Combine(_directory, "log_001.json")).Should().Contain("first");
    }

    private string[] RolledFiles()
        => Directory.GetFiles(_directory, "log_*.json");

    private static PragmaticProviderConfiguration Configuration()
    {
        var configuration = PragmaticJsonConfiguration.ForJson();
        configuration.IncludeContextEnrichment = false;
        return configuration;
    }

    // The local time zone is UTC, so the date the provider reads is the one set here on any machine.
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
