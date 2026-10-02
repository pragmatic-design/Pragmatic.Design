using Pragmatic.Testing.Assertions;
using Pragmatic.Composition.Hosting;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.Tests.Hosting;

public class MigrationProgressStreamTests
{
    [Fact]
    public async Task Report_SingleEvent_CanBeStreamed()
    {
        var stream = new MigrationProgressStream();
        var evt = new MigrationProgressEvent("migration", "Test message");

        stream.Report(evt);
        stream.Complete();

        var results = new List<MigrationProgressEvent>();
        await foreach (var item in stream.StreamAsync())
        {
            results.Add(item);
        }

        results.Should().ContainSingle()
            .Which.Message.Should().Be("Test message");
    }

    [Fact]
    public async Task Report_MultipleEvents_StreamedInOrder()
    {
        var stream = new MigrationProgressStream();

        stream.Report(new MigrationProgressEvent("migration", "Step 1"));
        stream.Report(new MigrationProgressEvent("migration", "Step 2"));
        stream.Report(new MigrationProgressEvent("complete", "Done"));
        stream.Complete();

        var results = new List<MigrationProgressEvent>();
        await foreach (var item in stream.StreamAsync())
        {
            results.Add(item);
        }

        results.Should().HaveCount(3);
        results[0].Message.Should().Be("Step 1");
        results[1].Message.Should().Be("Step 2");
        results[2].Message.Should().Be("Done");
    }

    [Fact]
    public async Task StreamAsync_Cancellation_StopsEnumeration()
    {
        var stream = new MigrationProgressStream();
        using var cts = new CancellationTokenSource();

        stream.Report(new MigrationProgressEvent("migration", "Event 1"));

        var results = new List<MigrationProgressEvent>();

#pragma warning disable CA2007 // xUnit manages SynchronizationContext
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in stream.StreamAsync(cts.Token))
            {
                results.Add(item);
                // Cancel after first event — stream is NOT complete, so it would block
                await cts.CancelAsync();
            }
        });
#pragma warning restore CA2007

        results.Should().ContainSingle();
    }

    [Fact]
    public void GetHistory_ReturnsAllReportedEvents()
    {
        var stream = new MigrationProgressStream();

        stream.Report(new MigrationProgressEvent("migration", "A"));
        stream.Report(new MigrationProgressEvent("migration", "B"));

        var history = stream.GetHistory();

        history.Should().HaveCount(2);
        history[0].Message.Should().Be("A");
        history[1].Message.Should().Be("B");
    }

    [Fact]
    public void GetHistory_ReturnsSnapshot_NotLiveReference()
    {
        var stream = new MigrationProgressStream();
        stream.Report(new MigrationProgressEvent("migration", "A"));

        var history1 = stream.GetHistory();
        stream.Report(new MigrationProgressEvent("migration", "B"));
        var history2 = stream.GetHistory();

        history1.Should().HaveCount(1);
        history2.Should().HaveCount(2);
    }

    [Fact]
    public void Report_WithProgressPercent_PreservedInEvent()
    {
        var stream = new MigrationProgressStream();
        var evt = new MigrationProgressEvent("migration", "Half done", ProgressPercent: 50.0, DatabaseName: "AppDb");

        stream.Report(evt);

        var history = stream.GetHistory();
        history[0].ProgressPercent.Should().Be(50.0);
        history[0].DatabaseName.Should().Be("AppDb");
    }

    [Fact]
    public void Report_ErrorEvent_PreservedInHistory()
    {
        var stream = new MigrationProgressStream();
        var evt = new MigrationProgressEvent("error", "Migration failed", IsError: true, ErrorDetail: "Connection timeout");

        stream.Report(evt);

        var history = stream.GetHistory();
        history[0].IsError.Should().BeTrue();
        history[0].ErrorDetail.Should().Be("Connection timeout");
    }

    [Fact]
    public void MigrationProgressEvent_Timestamp_DefaultsToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var evt = new MigrationProgressEvent("test", "msg");
        var after = DateTimeOffset.UtcNow;

        evt.Timestamp.Should().NotBeNull();
        evt.Timestamp!.Value.Should().BeOnOrAfter(before);
        evt.Timestamp!.Value.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void MigrationProgressEvent_ExplicitTimestamp_Preserved()
    {
        var ts = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var evt = new MigrationProgressEvent("test", "msg", Timestamp: ts);

        evt.Timestamp.Should().Be(ts);
    }

    [Fact]
    public async Task Report_ConcurrentProducers_AllEventsDelivered()
    {
        var stream = new MigrationProgressStream();
        var tasks = new List<Task>();

        for (var i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
                stream.Report(new MigrationProgressEvent("migration", $"Event {index}"))));
        }

        await Task.WhenAll(tasks);

        var history = stream.GetHistory();
        history.Should().HaveCount(50);
    }

    [Fact]
    public async Task Complete_CalledTwice_DoesNotThrow()
    {
        var stream = new MigrationProgressStream();
        stream.Report(new MigrationProgressEvent("test", "msg"));

        stream.Complete();
        var act = () => stream.Complete();

        act.Should().NotThrow();

        // Stream should still be consumable
        var results = new List<MigrationProgressEvent>();
        await foreach (var item in stream.StreamAsync())
        {
            results.Add(item);
        }

        results.Should().ContainSingle();
    }
}
