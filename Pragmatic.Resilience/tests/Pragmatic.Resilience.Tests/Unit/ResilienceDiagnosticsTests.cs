using System.Diagnostics;
using Pragmatic.Testing.Assertions;
using Pragmatic.Resilience.Diagnostics;
using Xunit;

namespace Pragmatic.Resilience.Tests.Unit;

/// <summary>
///     Tests for <see cref="ResilienceDiagnostics"/> static instruments and source configuration.
/// </summary>
public class ResilienceDiagnosticsTests
{
    [Fact]
    public void SourceName_IsPragmaticResilience()
    {
        ResilienceDiagnostics.SourceName.Should().Be("Pragmatic.Resilience");
    }

    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        ResilienceDiagnostics.ActivitySource.Name.Should().Be("Pragmatic.Resilience");
    }

    [Fact]
    public void ActivitySource_HasVersion()
    {
        ResilienceDiagnostics.ActivitySource.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Meter_HasCorrectName()
    {
        ResilienceDiagnostics.Meter.Name.Should().Be("Pragmatic.Resilience");
    }

    [Fact]
    public void PipelineDuration_IsNotNull()
    {
        ResilienceDiagnostics.PipelineDuration.Should().NotBeNull();
    }

    [Fact]
    public void PipelineExecutions_IsNotNull()
    {
        ResilienceDiagnostics.PipelineExecutions.Should().NotBeNull();
    }

    [Fact]
    public void RetryAttempts_IsNotNull()
    {
        ResilienceDiagnostics.RetryAttempts.Should().NotBeNull();
    }

    [Fact]
    public void CircuitRejections_IsNotNull()
    {
        ResilienceDiagnostics.CircuitRejections.Should().NotBeNull();
    }

    [Fact]
    public void Timeouts_IsNotNull()
    {
        ResilienceDiagnostics.Timeouts.Should().NotBeNull();
    }

    [Fact]
    public void BulkheadRejections_IsNotNull()
    {
        ResilienceDiagnostics.BulkheadRejections.Should().NotBeNull();
    }
}
