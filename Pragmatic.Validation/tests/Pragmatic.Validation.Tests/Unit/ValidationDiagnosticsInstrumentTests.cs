using System.Diagnostics;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Diagnostics;
using Xunit;

namespace Pragmatic.Validation.Tests.Unit;

/// <summary>
///     Tests for <see cref="ValidationDiagnostics"/> static instruments and source configuration.
/// </summary>
public class ValidationDiagnosticsInstrumentTests
{
    [Fact]
    public void SourceName_IsPragmaticValidation()
    {
        ValidationDiagnostics.SourceName.Should().Be("Pragmatic.Validation");
    }

    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        ValidationDiagnostics.ActivitySource.Name.Should().Be("Pragmatic.Validation");
    }

    [Fact]
    public void ActivitySource_HasVersion()
    {
        ValidationDiagnostics.ActivitySource.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Meter_HasCorrectName()
    {
        ValidationDiagnostics.Meter.Name.Should().Be("Pragmatic.Validation");
    }

    [Fact]
    public void ValidationDuration_IsNotNull()
    {
        ValidationDiagnostics.ValidationDuration.Should().NotBeNull();
    }

    [Fact]
    public void ValidationExecutions_IsNotNull()
    {
        ValidationDiagnostics.ValidationExecutions.Should().NotBeNull();
    }

    [Fact]
    public void ValidationFailures_IsNotNull()
    {
        ValidationDiagnostics.ValidationFailures.Should().NotBeNull();
    }
}
