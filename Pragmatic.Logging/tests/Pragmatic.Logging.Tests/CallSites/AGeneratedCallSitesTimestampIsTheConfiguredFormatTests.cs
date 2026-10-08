using System.Globalization;
using System.Text;
using System.Text.Json;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.CallSites;

/// <summary>
///     The <c>@timestamp</c> of a line a generated call site writes through the JSON provider is the moment of
///     the call, in the configured format: read back with that format, it falls between the instant before the
///     call and the instant after.
/// </summary>
/// <remarks>
///     The equivalence test with the classic path masks the timestamp, since the two lines are written at
///     different instants; this is the test that sees what the call-site path writes there.
/// </remarks>
public class AGeneratedCallSitesTimestampIsTheConfiguredFormatTests
{
    [Theory]
    [InlineData("yyyy-MM-ddTHH:mm:ss.fffZ")]
    [InlineData("yyyy-MM-dd HH:mm:ss.fff")]
    [InlineData("yyyy-MM-ddTHH:mm:ss.fffffff")]
    [InlineData("O")]
    public void TheTimestampReadsBackAsTheMomentOfTheCall(string format)
    {
        var config = PragmaticJsonConfiguration.ForJson();
        config.IncludeContextEnrichment = false;
        config.Formatting.TimestampFormat = format;
        config.Formatting.UseUtcTimestamp = true;

        var output = new MemoryStream();
        DateTime before, after;
        using (var provider = new PragmaticJsonProvider("json", config, output))
        {
            var logger = provider.CreateLogger("Orders");
            before = DateTime.UtcNow;
            OrderLog.ReceiptSent(logger, 42, "alice@example.com");
            after = DateTime.UtcNow;
        }

        using var line = JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()).Trim());
        var text = line.RootElement.GetProperty("@timestamp").GetString()!;
        var written = DateTime.ParseExact(
            text, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        // A format of milliseconds truncates the instant: the lower bound is the instant before, truncated alike.
        written.Should().BeOnOrAfter(before.AddTicks(-(before.Ticks % TimeSpan.TicksPerMillisecond)));
        written.Should().BeOnOrBefore(after);
    }
}
