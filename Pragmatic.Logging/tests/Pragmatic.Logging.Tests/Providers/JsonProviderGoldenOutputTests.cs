using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
///     What the JSON provider writes, pinned byte for byte, so that work on the pipeline's per-call costs
///     cannot change the output without a test saying so.
/// </summary>
/// <remarks>
///     <para>
///         Context enrichment is off, because its properties are the machine's and the process's, and the
///         timestamp is replaced before comparing. Everything else is the line as written: property order,
///         number and date formatting, escaping, the complex-value serialization.
///     </para>
///     <para>
///         The expected lines were taken from the provider as it was before that work started. A change to
///         them is a change to the output and has to be argued for as one.
///     </para>
///     <para>
///         ⚠️ No scope here: inside a scope the provider writes <c>"@scopes":["RequestId":"req-1",…]</c>,
///         which is not valid JSON, and a golden test would pin that defect. It is #90.
///     </para>
/// </remarks>
public partial class JsonProviderGoldenOutputTests
{
    private sealed record Order(int Id, string Customer, decimal Total);

    [Fact]
    public void ASimpleCall()
        => Lines(logger => logger.LogInformation("Order {OrderId} for {User}", 42, "jane"))
            .Should().Be("""{"@timestamp":"T","@level":"INFO","@logger":"Golden","@message":"Order 42 for jane","@messageTemplate":"Order {OrderId} for {User}","@properties":{"OrderId":42,"User":"jane"}}""");

    [Fact]
    public void ScalarsOfEveryShape()
        => Lines(logger => logger.LogInformation(
                "At {When} id {Id} took {Elapsed} cost {Cost} ok {Ok} note {Note}",
                new DateTime(2026, 10, 4, 12, 30, 0, DateTimeKind.Utc),
                new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
                new TimeSpan(1, 2, 3),
                12.5m,
                true,
                (string?)null))
            .Should().Be("""{"@timestamp":"T","@level":"INFO","@logger":"Golden","@message":"At 10/04/2026 12:30:00 id 6f9619ff-8b86-d011-b42d-00c04fc964ff took 01:02:03 cost 12.5 ok True note (null)","@messageTemplate":"At {When} id {Id} took {Elapsed} cost {Cost} ok {Ok} note {Note}","@properties":{"When":"2026-10-04T12:30:00.0000000Z","Id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","Elapsed":"01:02:03","Cost":12.5,"Ok":true,"Note":null}}""");

    [Fact]
    public void AComplexValue()
        => Lines(logger => logger.LogInformation("Placed {Order}", new Order(7, "Müller & Co <x>", 99.95m)))
            .Should().Be("""{"@timestamp":"T","@level":"INFO","@logger":"Golden","@message":"Placed Order { Id = 7, Customer = Müller & Co <x>, Total = 99.95 }","@messageTemplate":"Placed {Order}","@properties":{"Order":"{\"id\":7,\"customer\":\"Müller & Co <x>\",\"total\":99.95}"}}""");

    [Fact]
    public void AnException()
        => Lines(logger => logger.LogError(new InvalidOperationException("boom"), "Failed {Step}", "pay"))
            .Should().Be("""{"@timestamp":"T","@level":"FAIL","@logger":"Golden","@message":"Failed pay","@exception":{"type":"System.InvalidOperationException","message":"boom","stackTrace":null},"@messageTemplate":"Failed {Step}","@properties":{"Step":"pay"}}""");

    private static string Lines(Action<ILogger> log)
    {
        var config = PragmaticJsonConfiguration.ForJson();
        config.IncludeContextEnrichment = false;

        // A record's ToString() formats its members with the current culture, so the message would differ
        // between this machine and the CI's.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var output = new MemoryStream();
        try
        {
            using var provider = new PragmaticJsonProvider("json", config, output);
            log(provider.CreateLogger("Golden"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var written = Encoding.UTF8.GetString(output.ToArray()).Trim();
        return Timestamp().Replace(written, "\"@timestamp\":\"T\"");
    }

    [GeneratedRegex("\"@timestamp\":\"[^\"]*\"")]
    private static partial Regex Timestamp();
}
