using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Validation.Diagnostics;
using Pragmatic.Validation.Extensions;

namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     Observability: <see cref="ValidationDiagnostics" /> exposes an ActivitySource and a Meter.
///     Running validation through the <c>CompositeValidator</c> pipeline emits a span per call
///     plus execution/failure counters and a duration histogram — observable via the standard
///     <see cref="ActivityListener" /> and <see cref="MeterListener" /> APIs (the same hooks
///     OpenTelemetry uses).
/// </summary>
public static class DiagnosticsSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("11. ValidationDiagnostics — ActivitySource & Meter");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        await ShowObservedValidation();

        Console.WriteLine();
    }

    private static async Task ShowObservedValidation()
    {
        Console.WriteLine("  11.1 Listen to spans + metrics emitted by the validation pipeline");
        Console.WriteLine("  -------------------------------------------------------------------");

        var activityNames = new List<string>();
        long executionCount = 0;
        long failureCount = 0;
        double lastDurationMs = -1;

        // ── Trace: capture every Activity from the Pragmatic.Validation source ──
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == ValidationDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activityNames.Add(activity.DisplayName)
        };
        ActivitySource.AddActivityListener(activityListener);

        // ── Metrics: capture the validation instruments from the Pragmatic.Validation meter ──
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ValidationDiagnostics.SourceName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "pragmatic.validation.executions") executionCount += measurement;
            else if (instrument.Name == "pragmatic.validation.failures") failureCount += measurement;
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "pragmatic.validation.duration") lastDurationMs = measurement;
        });
        meterListener.Start();

        // Build a real validator and run two validations (one fails, one passes).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSyncOnlyValidator<CreateUserRequest>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<CreateUserRequest>>();

        var invalid = new CreateUserRequest { Email = "bad", Name = "A", Age = 5 };
        var valid = new CreateUserRequest { Email = "ok@example.com", Name = "Valid User", Age = 30 };

        var r1 = await validator.ValidateAsync(invalid);
        var r2 = await validator.ValidateAsync(valid);

        // Flush any pending observable measurements.
        meterListener.RecordObservableInstruments();

        Console.WriteLine($"    Validations run: invalid(IsFailure={r1.IsFailure}), valid(IsSuccess={r2.IsSuccess})");
        Console.WriteLine($"    Activities captured: {activityNames.Count} → [{string.Join(", ", activityNames)}]");
        Console.WriteLine($"    Counter pragmatic.validation.executions: {executionCount}");
        Console.WriteLine($"    Counter pragmatic.validation.failures:   {failureCount}");
        Console.WriteLine($"    Histogram last duration recorded:        {lastDurationMs >= 0}");
        Console.WriteLine();
        Console.WriteLine("    In production, register these with OpenTelemetry:");
        Console.WriteLine("      tracing.AddSource(ValidationDiagnostics.SourceName);");
        Console.WriteLine("      metrics.AddMeter(ValidationDiagnostics.SourceName);");
        Console.WriteLine();
    }
}
