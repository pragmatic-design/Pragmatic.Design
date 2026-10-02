using System.Diagnostics;
using Pragmatic.Testing.Assertions;
using Pragmatic.Logging.Diagnostics;
using Xunit;

namespace Pragmatic.Logging.Tests.Unit;

/// <summary>
///     Tests for <see cref="LoggingDiagnostics"/> static instruments and source configuration.
/// </summary>
public class LoggingDiagnosticsTests
{
    [Fact]
    public void SourceName_IsPragmaticLogging()
    {
        LoggingDiagnostics.SourceName.Should().Be("Pragmatic.Logging");
    }

    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        LoggingDiagnostics.ActivitySource.Name.Should().Be("Pragmatic.Logging");
    }

    [Fact]
    public void ActivitySource_HasVersion()
    {
        LoggingDiagnostics.ActivitySource.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Meter_HasCorrectName()
    {
        LoggingDiagnostics.Meter.Name.Should().Be("Pragmatic.Logging");
    }

    [Fact]
    public void LogThroughput_IsNotNull()
    {
        LoggingDiagnostics.LogThroughput.Should().NotBeNull();
    }

    [Fact]
    public void LogDrops_IsNotNull()
    {
        LoggingDiagnostics.LogDrops.Should().NotBeNull();
    }

    [Fact]
    public void BatchLatency_IsNotNull()
    {
        LoggingDiagnostics.BatchLatency.Should().NotBeNull();
    }

    [Fact]
    public void BatchSize_IsNotNull()
    {
        LoggingDiagnostics.BatchSize.Should().NotBeNull();
    }
}
