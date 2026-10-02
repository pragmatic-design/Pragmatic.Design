using System.Diagnostics;
using System.Diagnostics.Metrics;
using Pragmatic.Email.Diagnostics;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     OpenTelemetry surface of <see cref="EmailDiagnostics"/>: subscribing to the
///     <c>pragmatic.email.*</c> counters/histograms with a <see cref="MeterListener"/> and to the
///     <c>Pragmatic.Email</c> <see cref="ActivitySource"/> with an <see cref="ActivityListener"/> —
///     the same names a real OpenTelemetry exporter registers.
/// </summary>
public static class DiagnosticsSample
{
    public static Task Run()
    {
        Console.WriteLine("--- Diagnostics / OpenTelemetry ---");

        long sentTotal = 0;

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == EmailDiagnostics.SourceName)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "pragmatic.email.sent")
                sentTotal += measurement;
        });
        meterListener.Start();

        var activitiesStarted = 0;
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EmailDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => activitiesStarted++,
        };
        ActivitySource.AddActivityListener(activityListener);

        // Emit the instrumentation a send path would produce.
        using (var activity = EmailDiagnostics.ActivitySource.StartActivity("Email.Send", ActivityKind.Producer))
        {
            activity?.SetTag("email.subject", "Instrumented send");
            EmailDiagnostics.EmailsSent.Add(1, new KeyValuePair<string, object?>("transport", "in-memory"));
            EmailDiagnostics.EmailsSent.Add(2);
            EmailDiagnostics.SendDuration.Record(12.5);
        }

        Console.WriteLine($"  ActivitySource name          : {EmailDiagnostics.SourceName}");
        Console.WriteLine($"  Activities started           : {activitiesStarted}");
        Console.WriteLine($"  'pragmatic.email.sent' total : {sentTotal}");
        Console.WriteLine();

        return Task.CompletedTask;
    }
}
