using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Privacy;

/// <summary>
///     A property the redaction pipeline masks by its name stays out of the rendered message, not only out
///     of the structured properties.
/// </summary>
/// <remarks>
///     <para>
///         Through a real <see cref="ILogger" />, which renders the message from the template before the
///         pipeline runs. The pipeline used to mask the property and leave the message as rendered, so
///         <c>apiKey=sk_live_…</c> went out in clear beside <c>ApiKey="[REDACTED]"</c>. The message patterns
///         only caught what a regex happens to recognise, an email; a key matched by name was not.
///     </para>
///     <para>
///         Every provider writes through the same base pipeline, so the JSON provider, which writes the
///         message beside the properties, and the in-memory one stand for the others.
///     </para>
/// </remarks>
public class APropertyRedactedByNameReachesTheMessageTests
{
    private const string Secret = "sk_live_8f3b2c9e4d1a";

    [Fact]
    public void JsonProvider_AKeyMaskedByNameIsMaskedInTheMessageToo()
    {
        var line = LogThroughJson(logger =>
            logger.LogInformation("Outbound API call: endpoint={Endpoint}, apiKey={ApiKey}", "https://partner.example/v1", Secret));

        line.Should().NotContain(Secret);
        Message(line).Should().Be("Outbound API call: endpoint=https://partner.example/v1, apiKey=[REDACTED]");
    }

    [Fact]
    public void MemoryProvider_TheMessageAndThePropertyAgree()
    {
        var config = new PragmaticProviderConfiguration();
        config.Privacy.EnableRedaction = true;
        using var provider = new PragmaticMemoryProvider("memory", config);

        provider.CreateLogger("Test").LogInformation("Charged {OrderId} with key {ApiKey}", "ORD-1", Secret);

        var entry = provider.GetLogEntries().Single();
        entry.Properties["ApiKey"].Should().Be("[REDACTED]");
        entry.Message.Should().Be("Charged ORD-1 with key [REDACTED]");
    }

    /// <summary>
    ///     The control: an entry with nothing to mask keeps the message its own formatter rendered.
    /// </summary>
    [Fact]
    public void AnEntryWithNothingToMask_KeepsItsMessage()
    {
        var line = LogThroughJson(logger => logger.LogInformation("Placed {OrderId} for {Amount}", "ORD-7", 12.5m));

        Message(line).Should().Be("Placed ORD-7 for 12.5");
    }

    private static string LogThroughJson(Action<ILogger> log)
    {
        var config = PragmaticJsonConfiguration.ForJson();
        config.Privacy.EnableRedaction = true;

        var output = new MemoryStream();
        var provider = new PragmaticJsonProvider("json", config, output);

        log(provider.CreateLogger("Test"));
        provider.Dispose();

        return Encoding.UTF8.GetString(output.ToArray()).Trim();
    }

    private static string Message(string line)
        => JsonDocument.Parse(line).RootElement.GetProperty("@message").GetString()!;
}
