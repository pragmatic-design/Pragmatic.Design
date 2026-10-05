using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
///     A complex structured value is serialized from the application's JSON seam. When the seam has no
///     metadata for its type, the entry is still written, with the value's <c>ToString()</c>, and counted.
/// </summary>
/// <remarks>
///     <para>
///         The provider used to call <c>JsonSerializer.Serialize(value, options)</c>, reflection-based.
///         Under Native AOT that threw inside the provider and the entry was lost. A seam with its
///         reflection fallback turned off is that situation on the JIT.
///     </para>
///     <para>
///         The control is the same value through the default seam, which on the JIT ends in reflection:
///         written as JSON, nothing counted. It is what a JIT host wrote before.
///     </para>
/// </remarks>
public class AComplexValueWithoutMetadataIsWrittenAndCountedTests
{
    private sealed record Order(int Id, string Customer);

    [Fact]
    public void NoMetadata_TheEntryIsWrittenWithTheValuesToString_AndCounted()
    {
        var (line, metrics) = Log(new PragmaticJsonOptions().DisableReflectionFallback());

        line.Should().Contain("Order { Id = 7, Customer = acme }");
        metrics.ComplexValuesWithoutMetadata.Should().Be(1);
        metrics.FailedMessages.Should().Be(0);
    }

    [Fact]
    public void WithMetadata_TheValueIsWrittenAsJson_AndNothingIsCounted()
    {
        var (line, metrics) = Log(new PragmaticJsonOptions());

        line.Should().Contain("""{\"id\":7,\"customer\":\"acme\"}""");
        metrics.ComplexValuesWithoutMetadata.Should().Be(0);
    }

    private static (string Line, ProviderMetrics Metrics) Log(PragmaticJsonOptions seam)
    {
        var config = PragmaticJsonConfiguration.ForJson();
        config.IncludeContextEnrichment = false;

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var output = new MemoryStream();
        ProviderMetrics metrics;
        try
        {
            using var provider = new PragmaticJsonProvider("json", config, output) { JsonOptions = seam };
            provider.CreateLogger("Values").LogInformation("Placed {Order}", new Order(7, "acme"));
            metrics = provider.GetMetrics();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        return (Encoding.UTF8.GetString(output.ToArray()), metrics);
    }
}
