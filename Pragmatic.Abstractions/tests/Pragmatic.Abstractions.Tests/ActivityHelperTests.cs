using System.Diagnostics;
using Pragmatic.Testing.Assertions;
using Pragmatic.Telemetry;
using Pragmatic.Telemetry.Conventions;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class ActivityHelperTests : IDisposable
{
    private readonly ActivitySource _source = new("Pragmatic.Abstractions.Tests");
    private readonly ActivityListener _listener;

    public ActivityHelperTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Pragmatic.Abstractions.Tests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    [Fact]
    public void AllHelpers_OnNullActivity_ReturnNullWithoutThrowing()
    {
        Activity? activity = null;

        activity.RecordException(new InvalidOperationException("x")).Should().BeNull();
        activity.SetSuccess().Should().BeNull();
        activity.SetFailure("ERR").Should().BeNull();
        activity.AddNamedEvent("cache.hit").Should().BeNull();
    }

    [Fact]
    public void RecordException_SetsErrorStatusAndOtelEvent()
    {
        using var activity = _source.StartActivity("op");
        activity.Should().NotBeNull();

        activity.RecordException(new InvalidOperationException("boom"));

        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("boom");
        var evt = activity.Events.Should().ContainSingle(e => e.Name == "exception").Subject;

        // The literal OTel key, not the constant: an event carrying ErrorTags.Type would say
        // "error.type" — the span attribute for a kind of failure, not the exception-event key — and a
        // backend reading the event would find no type at all. Asserting the constant would follow
        // the constant wherever it went.
        evt.Tags.Should().Contain(t => t.Key == "exception.type"
            && (string?)t.Value == typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public void SetSuccess_SetsOkStatus()
    {
        using var activity = _source.StartActivity("op");

        activity.SetSuccess();

        activity!.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public void SetFailure_SetsErrorStatusAndErrorCodeTag()
    {
        using var activity = _source.StartActivity("op");

        activity.SetFailure("NOT_FOUND", "entity missing");

        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("entity missing");
        activity.GetTagItem(ErrorTags.Type).Should().Be("NOT_FOUND");
    }

    [Fact]
    public void SetFailure_WithoutDescription_UsesErrorCode()
    {
        using var activity = _source.StartActivity("op");

        activity.SetFailure("NOT_FOUND");

        activity!.StatusDescription.Should().Be("NOT_FOUND");
    }
}
