using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Jobs.Diagnostics;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Observability: subscribes to the <c>Pragmatic.Jobs</c> <see cref="Meter"/>
///     and <see cref="ActivitySource"/> exactly as an OpenTelemetry exporter
///     would, then runs a job and prints the metrics/spans the runtime emitted.
///     <para>
///         In production you would instead call (with the OpenTelemetry SDK):
///         <c>.WithMetrics(m => m.AddMeter(JobsDiagnostics.SourceName))</c> and
///         <c>.WithTracing(t => t.AddSource(JobsDiagnostics.SourceName))</c>.
///         This sample uses the BCL listeners so it stays zero-extra-dependency.
///     </para>
/// </summary>
public static class DiagnosticsSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Diagnostics (metrics + activity) ---");

        var counters = new ConcurrentDictionary<string, long>();
        var activities = new ConcurrentBag<string>();

        // --- Metrics listener: record every instrument on the Jobs meter. ---
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == JobsDiagnostics.SourceName)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            counters.AddOrUpdate(instrument.Name, value, (_, acc) => acc + value));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
            counters.AddOrUpdate($"{instrument.Name} (count)", 1, (_, acc) => acc + 1));
        meterListener.Start();

        // --- Activity listener: capture spans from the Jobs ActivitySource. ---
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == JobsDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
                activities.Add($"{activity.OperationName} [{activity.Status}] {activity.Duration.TotalMilliseconds:F1}ms"),
        };
        ActivitySource.AddActivityListener(activityListener);

        // --- Run a real job so the runtime emits enqueue/lease/complete signals. ---
        using var host = JobsHostBuilder.Build();
        await host.StartAsync();

        using (var scope = host.Services.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
            var store = host.Services.GetRequiredService<IJobStore>();
            var jobId = await scheduler.ScheduleAsync<SendInvoiceEmailJob>(correlationId: "otel-demo");
            await ScheduleAndAwaitSample.WaitForTerminal(store, jobId, TimeSpan.FromSeconds(15));
        }

        await host.StopAsync();
        meterListener.RecordObservableInstruments();

        Console.WriteLine("  captured metrics:");
        foreach (var (name, value) in counters.OrderBy(kvp => kvp.Key))
            Console.WriteLine($"    {name,-40} = {value}");

        Console.WriteLine("  captured activities (spans):");
        foreach (var span in activities)
            Console.WriteLine($"    {span}");

        Console.WriteLine();
    }
}
