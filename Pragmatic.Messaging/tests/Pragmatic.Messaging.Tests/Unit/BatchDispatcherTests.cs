using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Batch;
using Pragmatic.Messaging.Testing;

namespace Pragmatic.Messaging.Tests.Unit;

public class BatchDispatcherTests
{
    public record ImportBatch(List<string> Emails);

    public record ImportItem(string Email);

    private class ImportSplitter : IBatchSplitter<ImportBatch, ImportItem>
    {
        public IReadOnlyList<ImportItem> Split(ImportBatch batch)
            => batch.Emails.Select(e => new ImportItem(e)).ToList();
    }

    private static (BatchDispatcher<ImportBatch, ImportItem> dispatcher, MessageBusTestHarness bus, InMemoryBatchProgressStore store) CreateDispatcher()
    {
        var bus = new MessageBusTestHarness();
        var store = new InMemoryBatchProgressStore();
        var splitter = new ImportSplitter();
        var logger = NullLogger<BatchDispatcher<ImportBatch, ImportItem>>.Instance;
        var dispatcher = new BatchDispatcher<ImportBatch, ImportItem>(splitter, store, bus, logger);
        return (dispatcher, bus, store);
    }

    [Fact]
    public async Task DispatchAsync_PublishesAllItems()
    {
        var (dispatcher, bus, _) = CreateDispatcher();
        var batch = new ImportBatch(["alice@test.com", "bob@test.com", "carol@test.com"]);

        await dispatcher.DispatchAsync(batch);

        bus.Published.Should().HaveCount(3);
        var items = bus.PublishedOf<ImportItem>();
        items.Should().HaveCount(3);
        items.Select(i => i.Email).Should().BeEquivalentTo(
            ["alice@test.com", "bob@test.com", "carol@test.com"]);
    }

    [Fact]
    public async Task DispatchAsync_CreatesProgressEntry()
    {
        var (dispatcher, _, store) = CreateDispatcher();
        var batch = new ImportBatch(["alice@test.com", "bob@test.com"]);

        var batchId = await dispatcher.DispatchAsync(batch, label: "User import");

        var progress = await store.GetProgressAsync(batchId);
        progress.Should().NotBeNull();
        progress!.Total.Should().Be(2);
        progress.Completed.Should().Be(0);
        progress.Failed.Should().Be(0);
        progress.Label.Should().Be("User import");
        progress.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_SetsCorrectBatchHeaders()
    {
        var (dispatcher, bus, _) = CreateDispatcher();
        var batch = new ImportBatch(["alice@test.com", "bob@test.com"]);

        var batchId = await dispatcher.DispatchAsync(batch);

        bus.Published.Should().HaveCount(2);

        // ConcurrentBag does not guarantee order, so find items by index header
        var published = bus.Published
            .Where(p => p.Context?.Headers is not null)
            .OrderBy(p => p.Context!.Headers![BatchHeaders.ItemIndex])
            .ToList();
        published.Should().HaveCount(2);

        // Verify first item headers (index 0)
        var firstHeaders = published[0].Context!.Headers!;
        firstHeaders.Should().ContainKey(BatchHeaders.BatchId);
        firstHeaders[BatchHeaders.BatchId].Should().Be(batchId.ToString());
        firstHeaders[BatchHeaders.ItemIndex].Should().Be("0");
        firstHeaders[BatchHeaders.TotalItems].Should().Be("2");

        // Verify second item headers (index 1)
        var secondHeaders = published[1].Context!.Headers!;
        secondHeaders[BatchHeaders.BatchId].Should().Be(batchId.ToString());
        secondHeaders[BatchHeaders.ItemIndex].Should().Be("1");
        secondHeaders[BatchHeaders.TotalItems].Should().Be("2");
    }

    [Fact]
    public async Task DispatchAsync_EmptyBatch_CreatesZeroTotalProgress()
    {
        var (dispatcher, bus, store) = CreateDispatcher();
        var batch = new ImportBatch([]);

        var batchId = await dispatcher.DispatchAsync(batch);

        bus.Published.Should().BeEmpty();

        var progress = await store.GetProgressAsync(batchId);
        progress.Should().NotBeNull();
        progress!.Total.Should().Be(0);
        progress.IsComplete.Should().BeTrue();
        progress.CompletedAt.Should().NotBeNull("a zero-item batch is not active");
    }

    [Fact]
    public async Task DispatchAsync_Sequential_CheckpointsDispatchedCount()
    {
        var (dispatcher, _, store) = CreateDispatcher();
        var batch = new ImportBatch(["a@test.com", "b@test.com", "c@test.com"]);

        var batchId = await dispatcher.DispatchAsync(batch);

        var progress = await store.GetProgressAsync(batchId);
        progress!.DispatchedCount.Should().Be(3, "every published item advances the resume checkpoint");
    }

    [Fact]
    public async Task ResumeAsync_SkipsAlreadyDispatchedItems()
    {
        // A crash after publishing item 0 leaves DispatchedCount=1; resuming must publish only the
        // remaining items, not re-publish everything and not leave the batch orphaned.
        var (dispatcher, bus, store) = CreateDispatcher();
        var batch = new ImportBatch(["a@test.com", "b@test.com", "c@test.com"]);
        var batchId = Guid.NewGuid();
        await store.CreateAsync(new BatchProgress { BatchId = batchId, Total = 3, StartedAt = DateTimeOffset.UtcNow });
        await store.SetDispatchedCountAsync(batchId, 1); // crashed after publishing item 0

        var resumedId = await dispatcher.ResumeAsync(batch, batchId);

        resumedId.Should().Be(batchId);
        bus.PublishedOf<ImportItem>().Select(i => i.Email)
            .Should().BeEquivalentTo(["b@test.com", "c@test.com"], "only items after the checkpoint are re-published");
        (await store.GetProgressAsync(batchId))!.DispatchedCount.Should().Be(3);
    }

    [Fact]
    public async Task DispatchAsync_WithBoundedConcurrency_PublishesAllItems()
    {
        // MaxConcurrency > 1 publishes with bounded parallelism — every item still goes out.
        var bus = new MessageBusTestHarness();
        var store = new InMemoryBatchProgressStore();
        var dispatcher = new BatchDispatcher<ImportBatch, ImportItem>(
            new ImportSplitter(), store, bus,
            NullLogger<BatchDispatcher<ImportBatch, ImportItem>>.Instance,
            new BatchDispatchOptions { MaxConcurrency = 4 });
        var emails = Enumerable.Range(0, 20).Select(i => $"u{i}@test.com").ToList();

        await dispatcher.DispatchAsync(new ImportBatch(emails));

        bus.PublishedOf<ImportItem>().Should().HaveCount(20);
        bus.PublishedOf<ImportItem>().Select(i => i.Email).Should().BeEquivalentTo(emails);
    }
}
