using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Pragmatic.Testing.Assertions;
using Pragmatic.Jobs.Diagnostics;
using Xunit;

namespace Pragmatic.Jobs.Tests.Unit;

public class JobsDiagnosticsTests
{
    [Fact]
    public void SourceName_IsStable()
    {
        JobsDiagnostics.SourceName.Should().Be("Pragmatic.Jobs");
    }

    [Fact]
    public void ActivitySource_UsesSourceName()
    {
        JobsDiagnostics.ActivitySource.Name.Should().Be(JobsDiagnostics.SourceName);
    }

    [Fact]
    public void Meter_UsesSourceName()
    {
        JobsDiagnostics.Meter.Name.Should().Be(JobsDiagnostics.SourceName);
    }

    [Theory]
    [InlineData("pragmatic.jobs.enqueued")]
    [InlineData("pragmatic.jobs.completed")]
    [InlineData("pragmatic.jobs.failed")]
    [InlineData("pragmatic.jobs.retried")]
    [InlineData("pragmatic.jobs.duration")]
    [InlineData("pragmatic.jobs.lease_acquisitions")]
    [InlineData("pragmatic.jobs.lease_conflicts")]
    [InlineData("pragmatic.jobs.recurring_triggered")]
    public void Meter_ExposesInstrument(string instrumentName)
    {
        var names = new[]
        {
            JobsDiagnostics.JobsEnqueued.Name,
            JobsDiagnostics.JobsCompleted.Name,
            JobsDiagnostics.JobsFailed.Name,
            JobsDiagnostics.JobsRetried.Name,
            JobsDiagnostics.JobDuration.Name,
            JobsDiagnostics.LeaseAcquisitions.Name,
            JobsDiagnostics.LeaseConflicts.Name,
            JobsDiagnostics.RecurringJobsTriggered.Name,
        };

        names.Should().Contain(instrumentName);
    }

    [Fact]
    public void JobsEnqueued_Counter_IsObservedByMeterListener()
    {
        // Every measurement seen, not their sum: JobScheduler increments this same counter, and
        // xUnit runs test classes in parallel, so a scheduler test can land an Add(1) between our
        // Start() and our assertion. Summing made the expected total depend on what else was
        // running — green twice, red on the third gate.
        var seen = new ConcurrentBag<long>();

        // Read the counter before the listener exists. Creating an instrument publishes it, so a
        // listener attached first would be called back from inside the static constructor, while
        // the fields it reads are still null. Whether that happens depends on which test in the
        // process touched JobsDiagnostics first.
        var counter = JobsDiagnostics.JobsEnqueued;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == JobsDiagnostics.SourceName &&
                instrument.Name == counter.Name)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => seen.Add(measurement));
        listener.Start();

        counter.Add(3);

        // 3 is not a value JobScheduler can emit — it only ever adds 1 — so observing it means the
        // listener saw this test's measurement.
        seen.Should().Contain(3);
    }

    [Fact]
    public void JobDuration_Histogram_RecordsMeasurement()
    {
        // See JobsEnqueued_Counter_IsObservedByMeterListener for both reasons: the type must be
        // initialized before a listener can observe it half-built, and JobProcessorService records
        // on this same histogram, so the last value seen is not necessarily ours.
        var seen = new ConcurrentBag<double>();

        var histogram = JobsDiagnostics.JobDuration;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == JobsDiagnostics.SourceName &&
                instrument.Name == histogram.Name)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, measurement, _, _) => seen.Add(measurement));
        listener.Start();

        histogram.Record(12.5);

        seen.Should().Contain(12.5);
    }
}
