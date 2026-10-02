using System.Diagnostics;
using Pragmatic.Testing.Assertions;
using Pragmatic.Events.Diagnostics;
using Xunit;

namespace Pragmatic.Events.Tests.Unit;

/// <summary>
///     Tests for <see cref="EventsDiagnostics"/> static instruments and source configuration.
/// </summary>
public class EventsDiagnosticsTests
{
    [Fact]
    public void SourceName_IsPragmaticEvents()
    {
        EventsDiagnostics.SourceName.Should().Be("Pragmatic.Events");
    }

    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        EventsDiagnostics.ActivitySource.Name.Should().Be("Pragmatic.Events");
    }

    [Fact]
    public void ActivitySource_HasVersion()
    {
        EventsDiagnostics.ActivitySource.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Meter_HasCorrectName()
    {
        EventsDiagnostics.Meter.Name.Should().Be("Pragmatic.Events");
    }

    [Fact]
    public void DispatchDuration_IsNotNull()
    {
        EventsDiagnostics.DispatchDuration.Should().NotBeNull();
    }

    [Fact]
    public void EventsDispatched_IsNotNull()
    {
        EventsDiagnostics.EventsDispatched.Should().NotBeNull();
    }

    [Fact]
    public void HandlerFailures_IsNotNull()
    {
        EventsDiagnostics.HandlerFailures.Should().NotBeNull();
    }
}
