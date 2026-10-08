using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.CallSites;

/// <summary>
///     The JSON provider writes the parts of a generated call site's line that do not change between calls — the
///     level and the logger, the event and the template — from bytes it encoded once. Whatever the call site, the
///     line is the one the classic path writes for the same call, byte for byte.
/// </summary>
public partial class AGeneratedCallSitesJsonLineIsTheClassicOneTests
{
    [Fact]
    public void WithAnException_BetweenTheEventAndTheTemplate()
    {
        var failure = new InvalidOperationException("card declined");

        var generated = JsonLine(PragmaticJsonConfiguration.ForJson(), "Orders", logger => OrderLog.Failed(logger, failure, 42));
        var classic = JsonLine(PragmaticJsonConfiguration.ForJson(), "Orders", logger => logger.Log(
            LogLevel.Error, new EventId(1006, "Failed"), failure, OrderLog.FailedTemplate, 42));

        generated.Should().Contain("\"@exception\":").And.Be(classic);
    }

    [Fact]
    public void WithoutAnEventId_AndAtAnotherLevel()
    {
        var generated = JsonLine(PragmaticJsonConfiguration.ForJson(), "Stock", logger => OrderLog.StockLow(logger, "SKU-1"));
        var classic = JsonLine(PragmaticJsonConfiguration.ForJson(), "Stock", logger => logger.Log(
            LogLevel.Warning, new EventId(0, "StockLow"), OrderLog.StockLowTemplate, "SKU-1"));

        generated.Should().Contain("\"@level\":\"WARN\"").And.Be(classic);
    }

    [Fact]
    public void WithATemplateAndACategoryTheEncoderEscapes()
    {
        const string category = "Orders \"Café\" <EU>";

        var generated = JsonLine(PragmaticJsonConfiguration.ForJson(), category, logger => OrderLog.Quoted(logger, "a \"b\""));
        var classic = JsonLine(PragmaticJsonConfiguration.ForJson(), category, logger => logger.Log(
            LogLevel.Information, new EventId(1007, "Quoted"), OrderLog.QuotedTemplate, "a \"b\""));

        generated.Should().Contain("\\\"").And.Be(classic);
    }

    [Fact]
    public void PrettyPrinted()
    {
        var generated = JsonLine(PragmaticJsonConfiguration.ForPrettyJson(), "Orders", logger => OrderLog.ReceiptSent(logger, 42, "a@b.c"));
        var classic = JsonLine(PragmaticJsonConfiguration.ForPrettyJson(), "Orders", logger => logger.Log(
            LogLevel.Information, new EventId(1002, "ReceiptSent"), OrderLog.ReceiptTemplate, 42, "[redacted]"));

        generated.Should().Contain(Environment.NewLine).And.Be(classic);
    }

    /// <summary>
    ///     Loggers of different categories through one provider, interleaved: each line carries its own logger,
    ///     whichever wrote before it.
    /// </summary>
    [Fact]
    public void LoggersOfManyCategories_Interleaved_EachWriteTheirOwn()
    {
        var output = new MemoryStream();
        using (var provider = new PragmaticJsonProvider("json", Configuration(PragmaticJsonConfiguration.ForJson()), output))
        {
            var loggers = Enumerable.Range(0, 200).Select(i => provider.CreateLogger($"Category{i}")).ToArray();
            for (var round = 0; round < 3; round++)
            {
                foreach (var logger in loggers)
                    OrderLog.StockLow(logger, "SKU-1");
            }
        }

        var lines = Encoding.UTF8.GetString(output.ToArray()).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(600);
        for (var i = 0; i < lines.Length; i++)
            lines[i].Should().Contain($"\"@logger\":\"Category{i % 200}\"");
    }

    /// <summary>
    ///     The same loggers at steady state: each category's block is encoded once, however many categories share
    ///     the provider and in whatever order they write.
    /// </summary>
    [Fact]
    public void LoggersOfManyCategories_Interleaved_AllocateNothingAtSteadyState()
    {
        using var provider = new PragmaticJsonProvider("json", Configuration(PragmaticJsonConfiguration.ForJson()), Stream.Null);
        var loggers = Enumerable.Range(0, 200).Select(i => provider.CreateLogger($"Category{i}")).ToArray();

        foreach (var logger in loggers)
            OrderLog.StockLow(logger, "SKU-1");

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var round = 0; round < 5; round++)
        {
            foreach (var logger in loggers)
                OrderLog.StockLow(logger, "SKU-1");
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }

    private static PragmaticProviderConfiguration Configuration(PragmaticProviderConfiguration config)
    {
        config.IncludeContextEnrichment = false;
        return config;
    }

    private static string JsonLine(PragmaticProviderConfiguration config, string category, Action<ILogger> log)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var output = new MemoryStream();
        try
        {
            using var provider = new PragmaticJsonProvider("json", Configuration(config), output);
            log(provider.CreateLogger(category));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        return Timestamp().Replace(Encoding.UTF8.GetString(output.ToArray()).Trim(), "\"@timestamp\":\"T\"");
    }

    [GeneratedRegex("\"@timestamp\":\\s*\"[^\"]*\"")]
    private static partial Regex Timestamp();
}
