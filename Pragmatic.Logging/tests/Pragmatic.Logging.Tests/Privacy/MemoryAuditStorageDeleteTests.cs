using Pragmatic.Testing.Assertions;
using Pragmatic.Logging.Privacy;
using Pragmatic.Logging.Privacy.Audit.Storage;
using Xunit;

namespace Pragmatic.Logging.Tests.Privacy;

/// <summary>
/// Repro for the prior High finding "MemoryAuditStorage.DeleteEntriesAsync counts but never removes
/// entries". This test fails against that original behavior (remaining count would stay at 3).
/// </summary>
public class MemoryAuditStorageDeleteTests
{
    private static AuditEntry EntryAt(DateTime timestamp) => new()
    {
        Timestamp = timestamp,
        EventType = AuditEventType.DataRedaction,
        Severity = AuditSeverity.Low
    };

    [Fact]
    public async Task DeleteEntriesAsync_RemovesEntriesBeforeCutoff_AndKeepsTheRest()
    {
        var storage = new MemoryAuditStorage();
        var cutoff = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        await storage.StoreEntriesAsync(
        [
            EntryAt(cutoff.AddDays(-2)),
            EntryAt(cutoff.AddDays(-1)),
            EntryAt(cutoff.AddDays(1)),
        ]);

        var deleted = await storage.DeleteEntriesAsync(cutoff);

        deleted.Should().Be(2, "two entries predate the cutoff");

        // The load-bearing assertion: the entries are ACTUALLY gone, not just counted.
        var remaining = await storage.GetEntriesAsync(DateTime.MinValue, DateTime.MaxValue);
        remaining.Should().ContainSingle();
        remaining[0].Timestamp.Should().Be(cutoff.AddDays(1));

        var count = await storage.GetEntryCountAsync(DateTime.MinValue, DateTime.MaxValue);
        count.Should().Be(1);
    }

    [Fact]
    public async Task DeleteEntriesAsync_WhenNothingPredatesCutoff_RemovesNothing()
    {
        var storage = new MemoryAuditStorage();
        var cutoff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await storage.StoreEntriesAsync([EntryAt(cutoff.AddDays(1)), EntryAt(cutoff.AddDays(2))]);

        var deleted = await storage.DeleteEntriesAsync(cutoff);

        deleted.Should().Be(0);
        (await storage.GetEntryCountAsync(DateTime.MinValue, DateTime.MaxValue)).Should().Be(2);
    }
}
