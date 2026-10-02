using System.Diagnostics.Metrics;
using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Diagnostics;

namespace Pragmatic.Email.Tests.Unit.Diagnostics;

public sealed class EmailDiagnosticsTests
{
    [Fact]
    public void SourceName_IsStable()
    {
        EmailDiagnostics.SourceName.Should().Be("Pragmatic.Email");
    }

    [Fact]
    public void ActivitySource_UsesSourceName()
    {
        EmailDiagnostics.ActivitySource.Name.Should().Be(EmailDiagnostics.SourceName);
    }

    [Fact]
    public void Meter_UsesSourceName()
    {
        EmailDiagnostics.Meter.Name.Should().Be(EmailDiagnostics.SourceName);
    }

    [Fact]
    public void Counters_HaveExpectedInstrumentNames()
    {
        EmailDiagnostics.EmailsSent.Name.Should().Be("pragmatic.email.sent");
        EmailDiagnostics.EmailsFailed.Name.Should().Be("pragmatic.email.failed");
        EmailDiagnostics.SendDuration.Name.Should().Be("pragmatic.email.send.duration");
        EmailDiagnostics.ConnectionsCreated.Name.Should().Be("pragmatic.email.connections.created");
        EmailDiagnostics.ConnectionsRecycled.Name.Should().Be("pragmatic.email.connections.recycled");
    }

    [Fact]
    public void EmailsSent_Counter_IsObservableByMeterListener()
    {
        // Concurrent, not a List: the sender now records real sends, so the callback fires from every
        // thread that happens to be sending while this test runs. A plain List loses items — or worse —
        // under that race, which made the assertion fail intermittently.
        var captured = new System.Collections.Concurrent.ConcurrentQueue<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == EmailDiagnostics.SourceName && instrument.Name == "pragmatic.email.sent")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>(
            (_, measurement, _, _) => captured.Enqueue(measurement));
        listener.Start();

        EmailDiagnostics.EmailsSent.Add(3);

        // Asserted on the individual measurement, not on the sum: the Meter is process-wide and the
        // sender now genuinely records sends, so other tests running concurrently also contribute.
        captured.Should().Contain(3);
    }
}
