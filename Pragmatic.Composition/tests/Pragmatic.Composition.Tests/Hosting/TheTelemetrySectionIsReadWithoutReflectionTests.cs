using Microsoft.Extensions.Configuration;
using Pragmatic.Telemetry;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     The generated host reads the <c>Telemetry</c> section with <see cref="TelemetryOptions.ReadFrom" />, typed code, and not with
///     <c>ConfigurationBinder.Bind</c>, which binds by reflection (#857). It reads what the binder read, key for key.
/// </summary>
/// <remarks>
///     The binder's result is the reference: this suite runs on the JIT, where it works, and the two have to agree on every key, on a
///     section that sets them all, on one that sets none, on keys in another case, and on a value that is not one.
/// </remarks>
public sealed class TheTelemetrySectionIsReadWithoutReflectionTests
{
    private static IConfigurationSection Section(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Telemetry");

    [Fact]
    public void EveryKey_IsReadAsTheBinderReadsIt()
    {
        var section = Section(new()
        {
            ["Telemetry:Enabled"] = "false",
            ["Telemetry:Tracing"] = "false",
            ["Telemetry:Metrics"] = "false",
            ["Telemetry:Logging"] = "false",
            ["Telemetry:UseOtlpExporter"] = "true",
            ["Telemetry:ServiceName"] = "orders",
            ["Telemetry:SamplingRatio"] = "0.25",
        });

        ReadByBoth(section).Should().Be(("False|False|False|False|True|orders|0.25", "False|False|False|False|True|orders|0.25"));
    }

    [Fact]
    public void AnEmptySection_LeavesTheDefaults()
        => ReadByBoth(Section([])).Should().Be(("True|True|True|True|False||0.1", "True|True|True|True|False||0.1"));

    [Fact]
    public void KeysInAnotherCase_AreRead()
    {
        var section = Section(new() { ["telemetry:tracing"] = "FALSE", ["TELEMETRY:SERVICENAME"] = "billing" });

        ReadByBoth(section).Should().Be(("True|False|True|True|False|billing|0.1", "True|False|True|True|False|billing|0.1"));
    }

    [Fact]
    public void AValueThatIsNotOne_IsRefused()
    {
        var act = () => new TelemetryOptions().ReadFrom(Section(new() { ["Telemetry:Tracing"] = "sometimes" }));

        act.Should().Throw<InvalidOperationException>().Where(e => e.Message.Contains("Telemetry:Tracing", StringComparison.Ordinal));
    }

    [Fact]
    public void ARatioOutOfRange_IsRefused_AsTheSetterRefusesIt()
    {
        var act = () => new TelemetryOptions().ReadFrom(Section(new() { ["Telemetry:SamplingRatio"] = "1.5" }));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>The section read by the binder and by ReadFrom, each written out as one line to compare.</summary>
    private static (string Binder, string Typed) ReadByBoth(IConfigurationSection section)
    {
        var bound = new TelemetryOptions();
        section.Bind(bound);

        var read = new TelemetryOptions();
        read.ReadFrom(section);

        return (Line(bound), Line(read));
    }

    private static string Line(TelemetryOptions o)
        => string.Join('|', o.Enabled, o.Tracing, o.Metrics, o.Logging, o.UseOtlpExporter, o.ServiceName,
            o.SamplingRatio.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
