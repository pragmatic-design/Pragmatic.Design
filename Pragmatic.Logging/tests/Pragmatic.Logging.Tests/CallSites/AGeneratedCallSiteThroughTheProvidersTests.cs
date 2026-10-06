using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Redaction;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.CallSites;

/// <summary>
///     A generated call site, logged through the providers an application has: what each one writes, that
///     a personal-data argument is in clear in none of them, and what the JSON provider costs per call.
/// </summary>
public partial class AGeneratedCallSiteThroughTheProvidersTests
{
    private const string Email = "alice@example.com";
    private static readonly DateTime PlacedAt = new(2026, 10, 6, 9, 30, 0, DateTimeKind.Utc);

    // ── The JSON provider writes the line the classic path writes ───────────────────────────────────

    /// <summary>
    ///     The call-site path writes the state itself; the classic one renders it through the formatter and
    ///     a dictionary. The same call, the same line: anything else is a second output format.
    /// </summary>
    [Fact]
    public void TheJsonLineIsTheOneTheClassicPathWritesForTheSameCall()
    {
        var generated = JsonLine(logger => OrderLog.Placed(logger, 42, "jane", 19.99m, PlacedAt));
        var classic = JsonLine(logger => logger.Log(
            LogLevel.Information, new EventId(1001, "Placed"), OrderLog.PlacedTemplate, 42, "jane", 19.99m, PlacedAt));

        generated.Should().Be(classic);
    }

    [Fact]
    public void AMaskedArgumentIsMaskedInTheJsonLine()
    {
        var line = JsonLine(logger => OrderLog.ReceiptSent(logger, 42, Email));

        line.Should().NotContain(Email)
            .And.Contain("\"@message\":\"Receipt for 42 sent to [redacted]\"")
            .And.Contain("\"@properties\":{\"OrderId\":42,\"Email\":\"[redacted]\"}");
    }

    // ── In clear in no provider ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void APersonalDataArgumentIsInClearInNoProvider()
    {
        var json = new MemoryStream();
        var memory = new PragmaticMemoryProvider("memory", PragmaticMemoryConfiguration.ForMemory());
        var thirdParty = new ThirdPartyProvider();

        // A provider handed to AddProvider is not disposed by the factory, and the JSON configuration does
        // not flush per line: disposing it is what puts the line in the stream.
        using (var jsonProvider = new PragmaticJsonProvider("json", JsonConfiguration(), json))
        using (var factory = LoggerFactory.Create(builder => builder
                   .SetMinimumLevel(LogLevel.Trace)
                   .AddProvider(jsonProvider)
                   .AddProvider(memory)
                   .AddProvider(thirdParty)))
        {
            OrderLog.ReceiptSent(factory.CreateLogger("Orders"), 42, Email);
        }

        var entry = memory.GetLogEntries().Should().ContainSingle().Which;
        string[] outputs =
        [
            Encoding.UTF8.GetString(json.ToArray()),
            entry.Message,
            string.Join(";", entry.Properties.Select(p => $"{p.Key}={p.Value}")),
            thirdParty.Written.Should().ContainSingle().Which,
        ];

        foreach (var output in outputs)
            output.Should().NotContain(Email).And.Contain("[redacted]");
    }

    // ── What a call costs ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThroughTheJsonProvider_ACallAllocatesNothingAtSteadyState(bool declaredRedactor)
    {
        using var provider = new PragmaticJsonProvider("json", JsonConfiguration(), Stream.Null);
        if (declaredRedactor)
            provider.DeclaredRedactor = new DeclaredRedactor([new OneTypeRedactionMap()]);

        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("Orders");

        for (var i = 0; i < 200; i++)
            LogBoth(logger, i);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            LogBoth(logger, i);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0, "a call site whose arguments are values and strings, masked or not, writes its state itself");
    }

    private static void LogBoth(ILogger logger, int i)
    {
        OrderLog.Placed(logger, i, "jane", 19.99m, PlacedAt);
        OrderLog.ReceiptSent(logger, i, Email);
    }

    private static PragmaticProviderConfiguration JsonConfiguration()
    {
        var config = PragmaticJsonConfiguration.ForJson();
        // Context enrichment adds the machine's and the process's properties, read into a dictionary per
        // call; the comparisons here are about the call's own state.
        config.IncludeContextEnrichment = false;
        return config;
    }

    private static string JsonLine(Action<ILogger> log)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var output = new MemoryStream();
        try
        {
            using var provider = new PragmaticJsonProvider("json", JsonConfiguration(), output);
            log(provider.CreateLogger("Orders"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        return Timestamp().Replace(Encoding.UTF8.GetString(output.ToArray()).Trim(), "\"@timestamp\":\"T\"");
    }

    [GeneratedRegex("\"@timestamp\":\"[^\"]*\"")]
    private static partial Regex Timestamp();
}
