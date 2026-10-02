using System.Diagnostics.Metrics;
using Pragmatic.Notifications.Diagnostics;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     NotificationsDiagnostics exposes an OpenTelemetry Meter ("Pragmatic.Notifications") with
///     counters and a histogram for sent/failed/enqueued notifications and per-channel delivery.
///     In production these feed an OTel exporter (Prometheus, OTLP, ...). This sample attaches a
///     MeterListener to that meter — exactly how an exporter subscribes — records a few measurements
///     on the real instruments, and prints the aggregated values, showing the metric names callers
///     can scrape.
/// </summary>
public static class DiagnosticsMetricsSample
{
    public static void Run()
    {
        Console.WriteLine("--- NotificationsDiagnostics / OTel metrics ---");

        var totals = new Dictionary<string, long>();
        var durations = new List<double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            // Subscribe only to this module's meter.
            if (instrument.Meter.Name == NotificationsDiagnostics.SourceName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            totals.TryGetValue(instrument.Name, out var current);
            totals[instrument.Name] = current + value;
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => durations.Add(value));
        listener.Start();

        // Simulate what the pipeline emits during real sends.
        NotificationsDiagnostics.NotificationsSent.Add(3);
        NotificationsDiagnostics.NotificationsFailed.Add(1);
        NotificationsDiagnostics.NotificationsEnqueued.Add(2);
        NotificationsDiagnostics.ChannelDeliveries.Add(3, new KeyValuePair<string, object?>("channel", "Email"));
        NotificationsDiagnostics.ChannelFailures.Add(1, new KeyValuePair<string, object?>("channel", "Webhook"));
        NotificationsDiagnostics.DeliveryDuration.Record(12.5);
        NotificationsDiagnostics.DeliveryDuration.Record(8.0);

        listener.RecordObservableInstruments();

        Console.WriteLine($"  meter source : {NotificationsDiagnostics.SourceName}");
        foreach (var (name, value) in totals.OrderBy(kvp => kvp.Key))
            Console.WriteLine($"    {name,-40} = {value}");
        Console.WriteLine($"    {"pragmatic.notifications.delivery.duration",-40} = {durations.Count} samples, avg {durations.Average():F1} ms");
        Console.WriteLine();
    }
}
