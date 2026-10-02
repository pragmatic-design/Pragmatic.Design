using Pragmatic.Testing.Assertions;
using Pragmatic.Telemetry;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class TelemetryOptionsTests
{
    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void SamplingRatio_OutOfRange_Throws(double value)
    {
        var options = new TelemetryOptions();
        var act = () => options.SamplingRatio = value;
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(0.25)]
    public void SamplingRatio_InRange_Persists(double value)
    {
        var options = new TelemetryOptions { SamplingRatio = value };
        options.SamplingRatio.Should().Be(value);
    }
}
