using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Diagnostics;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Result;
using Pragmatic.Telemetry.Conventions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Tests that action and void action invokers emit correct Activity traces and metrics.
/// </summary>
[Collection("Telemetry")]
public class InvokerTelemetryTests : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;
    private readonly ConcurrentBag<Activity> _capturedActivities = [];
    private readonly ConcurrentBag<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> _capturedMetrics = [];

    public InvokerTelemetryTests()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActionsDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _capturedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener();
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ActionsDiagnostics.SourceName)
                listener.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            _capturedMetrics.Add((instrument.Name, measurement, tags.ToArray()));
        });
        _meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            _capturedMetrics.Add((instrument.Name, measurement, tags.ToArray()));
        });
        _meterListener.Start();
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    // =========================================================================
    // Test doubles
    // =========================================================================

    private sealed class SuccessAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    private sealed class SuccessActionInvoker(IServiceProvider sp)
        : DomainActionInvoker<SuccessAction, string>(sp)
    {
        protected override void InjectDependencies(SuccessAction action) { }
    }

    private sealed class FailAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Failure(new TestError("BIZ_ERROR")));
    }

    private sealed class FailActionInvoker(IServiceProvider sp)
        : DomainActionInvoker<FailAction, string>(sp)
    {
        protected override void InjectDependencies(FailAction action) { }
    }

    private sealed class ThrowAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => throw new InvalidOperationException("Boom!");
    }

    private sealed class ThrowActionInvoker(IServiceProvider sp)
        : DomainActionInvoker<ThrowAction, string>(sp)
    {
        protected override void InjectDependencies(ThrowAction action) { }
    }

    private sealed class VoidSuccessAction : VoidDomainAction
    {
        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(VoidResult<IError>.Success());
    }

    private sealed class VoidSuccessInvoker(IServiceProvider sp)
        : VoidDomainActionInvoker<VoidSuccessAction>(sp)
    {
        protected override void InjectDependencies(VoidSuccessAction action) { }
    }

    private sealed class TestError(string code) : IError
    {
        public string Code { get; } = code;
        public int StatusCode => 400;
        public string Title => Code;
    }

    private sealed class ShortCircuitFilter : IActionFilter
    {
        public int Order => 0;

        public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(TAction action, CancellationToken ct)
            where TAction : DomainAction<TReturn>
            => Task.FromResult(VoidResult<IError>.Failure(new TestError("BLOCKED")));

        public Task AfterExecuteAsync<TAction, TReturn>(TAction action, Result<TReturn, IError> result, CancellationToken ct)
            where TAction : DomainAction<TReturn>
            => Task.CompletedTask;
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // =========================================================================
    // Tests — Activity tracing
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_SuccessfulAction_CreatesActivityWithCorrectTags()
    {
        using var provider = BuildProvider();
        var invoker = new SuccessActionInvoker(provider);

        await invoker.InvokeAsync(new SuccessAction());

        var activity = _capturedActivities.FirstOrDefault(a => a.DisplayName.Contains("SuccessAction"));
        activity.Should().NotBeNull();

        activity!.GetTagItem(ActionTags.Name).Should().NotBeNull();
        activity!.GetTagItem(ActionTags.Kind)!.ToString().Should().Be("action");
        activity!.GetTagItem(ActionTags.Result)!.ToString().Should().Be("success");
        activity!.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task InvokeAsync_FailingAction_SetsFailureTagsOnActivity()
    {
        using var provider = BuildProvider();
        var invoker = new FailActionInvoker(provider);

        await invoker.InvokeAsync(new FailAction());

        var activity = _capturedActivities.FirstOrDefault(a => a.DisplayName.Contains("FailAction"));
        activity.Should().NotBeNull();

        activity!.GetTagItem(ActionTags.Result)!.ToString().Should().Be("failure");
        activity!.GetTagItem(ActionTags.ErrorCode)!.ToString().Should().Be("BIZ_ERROR");
        activity!.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task InvokeAsync_ThrowingAction_RecordsExceptionOnActivity()
    {
        using var provider = BuildProvider();
        var invoker = new ThrowActionInvoker(provider);

        try { await invoker.InvokeAsync(new ThrowAction()); } catch { /* expected */ }

        var activity = _capturedActivities.FirstOrDefault(a => a.DisplayName.Contains("ThrowAction"));
        activity.Should().NotBeNull();

        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity!.Events.Should().Contain(e => e.Name == "exception");
    }

    [Fact]
    public async Task InvokeAsync_VoidAction_CreatesActivityWithVoidActionKind()
    {
        using var provider = BuildProvider();
        var invoker = new VoidSuccessInvoker(provider);

        await invoker.InvokeAsync(new VoidSuccessAction());

        var activity = _capturedActivities.FirstOrDefault(a => a.DisplayName.Contains("VoidSuccessAction"));
        activity.Should().NotBeNull();

        activity!.GetTagItem(ActionTags.Kind)!.ToString().Should().Be("void_action");
        activity!.GetTagItem(ActionTags.Result)!.ToString().Should().Be("success");
    }

    [Fact]
    public async Task InvokeAsync_FilterShortCircuit_SetsShortCircuitedResult()
    {
        using var provider = BuildProvider(s =>
            s.AddSingleton<IActionFilter>(new ShortCircuitFilter()));
        var invoker = new SuccessActionInvoker(provider);

        await invoker.InvokeAsync(new SuccessAction());

        var activity = _capturedActivities.FirstOrDefault(a =>
            a.DisplayName.Contains("SuccessAction") &&
            a.GetTagItem(ActionTags.Result)?.ToString() == "short-circuited");
        activity.Should().NotBeNull();

        activity!.GetTagItem(ActionTags.ErrorCode)!.ToString().Should().Be("BLOCKED");
    }

    // =========================================================================
    // Tests — Metrics
    //
    // MeterListener captures metrics from all parallel tests using the same
    // static Meter. We filter by tag values specific to our test actions.
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_SuccessfulAction_RecordsInvocationAndDuration()
    {
        using var provider = BuildProvider();
        var invoker = new SuccessActionInvoker(provider);

        await invoker.InvokeAsync(new SuccessAction());
        _meterListener.RecordObservableInstruments();

        // Filter metrics to those from our specific action type
        var invocations = _capturedMetrics.Where(m =>
            m.Name == "pragmatic.actions.invocations" &&
            m.Tags.Any(t => t.Key == "action.name" && t.Value!.ToString() == "SuccessAction")).ToList();
        invocations.Should().NotBeEmpty("an invocation metric should be recorded for SuccessAction");

        var durations = _capturedMetrics.Where(m =>
            m.Name == "pragmatic.actions.duration" &&
            m.Tags.Any(t => t.Key == "action.name" && t.Value!.ToString() == "SuccessAction") &&
            m.Tags.Any(t => t.Key == "action.result" && t.Value!.ToString() == "success")).ToList();
        durations.Should().NotBeEmpty("a duration metric should be recorded for SuccessAction");
        durations.First().Value.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task InvokeAsync_FailingAction_RecordsFailureMetric()
    {
        using var provider = BuildProvider();
        var invoker = new FailActionInvoker(provider);

        await invoker.InvokeAsync(new FailAction());
        _meterListener.RecordObservableInstruments();

        var failures = _capturedMetrics.Where(m =>
            m.Name == "pragmatic.actions.failures" &&
            m.Tags.Any(t => t.Key == "error.code" && t.Value!.ToString() == "BIZ_ERROR")).ToList();
        failures.Should().NotBeEmpty("a failure metric should be recorded with BIZ_ERROR code");
    }

    [Fact]
    public async Task InvokeAsync_ThrowingAction_RecordsExceptionMetrics()
    {
        using var provider = BuildProvider();
        var invoker = new ThrowActionInvoker(provider);

        try { await invoker.InvokeAsync(new ThrowAction()); } catch { /* expected */ }
        _meterListener.RecordObservableInstruments();

        var failures = _capturedMetrics.Where(m =>
            m.Name == "pragmatic.actions.failures" &&
            m.Tags.Any(t => t.Key == "action.name" && t.Value!.ToString() == "ThrowAction")).ToList();
        failures.Should().NotBeEmpty("a failure metric should be recorded for ThrowAction");

        var durations = _capturedMetrics.Where(m =>
            m.Name == "pragmatic.actions.duration" &&
            m.Tags.Any(t => t.Key == "action.name" && t.Value!.ToString() == "ThrowAction") &&
            m.Tags.Any(t => t.Key == "action.result" && t.Value!.ToString() == "exception")).ToList();
        durations.Should().NotBeEmpty("a duration metric with 'exception' result should be recorded");
    }

    [Fact]
    public async Task InvokeAsync_FilterShortCircuit_RecordsShortCircuitMetric()
    {
        using var provider = BuildProvider(s =>
            s.AddSingleton<IActionFilter>(new ShortCircuitFilter()));
        var invoker = new SuccessActionInvoker(provider);

        await invoker.InvokeAsync(new SuccessAction());
        _meterListener.RecordObservableInstruments();

        var shortCircuits = _capturedMetrics.Where(m =>
            m.Name == "pragmatic.actions.filter_short_circuits").ToList();
        shortCircuits.Should().NotBeEmpty("a short-circuit metric should be recorded");
    }
}
