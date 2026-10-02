using System.Diagnostics;
using Pragmatic.Testing.Assertions;
using Pragmatic.Actions.Diagnostics;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Tests for <see cref="ActionsDiagnostics"/> static instruments and source configuration.
/// </summary>
public class ActionsDiagnosticsTests
{
    [Fact]
    public void SourceName_IsPragmaticActions()
    {
        ActionsDiagnostics.SourceName.Should().Be("Pragmatic.Actions");
    }

    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        ActionsDiagnostics.ActivitySource.Name.Should().Be("Pragmatic.Actions");
    }

    [Fact]
    public void ActivitySource_HasVersion()
    {
        ActionsDiagnostics.ActivitySource.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Meter_HasCorrectName()
    {
        ActionsDiagnostics.Meter.Name.Should().Be("Pragmatic.Actions");
    }

    [Fact]
    public void ActionDuration_IsNotNull()
    {
        ActionsDiagnostics.ActionDuration.Should().NotBeNull();
    }

    [Fact]
    public void ActionInvocations_IsNotNull()
    {
        ActionsDiagnostics.ActionInvocations.Should().NotBeNull();
    }

    [Fact]
    public void ActionFailures_IsNotNull()
    {
        ActionsDiagnostics.ActionFailures.Should().NotBeNull();
    }

    [Fact]
    public void FilterShortCircuits_IsNotNull()
    {
        ActionsDiagnostics.FilterShortCircuits.Should().NotBeNull();
    }

    [Fact]
    public void MutationDuration_IsNotNull()
    {
        ActionsDiagnostics.MutationDuration.Should().NotBeNull();
    }

    [Fact]
    public void MutationInvocations_IsNotNull()
    {
        ActionsDiagnostics.MutationInvocations.Should().NotBeNull();
    }

    [Fact]
    public void MutationFailures_IsNotNull()
    {
        ActionsDiagnostics.MutationFailures.Should().NotBeNull();
    }

    [Fact]
    public void MutationValidationFailures_IsNotNull()
    {
        ActionsDiagnostics.MutationValidationFailures.Should().NotBeNull();
    }
}
