using Pragmatic.Testing.Assertions;

namespace Pragmatic.Audit.Tests;

/// <summary>
///     The shaping every writer must apply, tested where it now lives rather than through one store.
/// </summary>
/// <remarks>
///     It was extracted because there is more than one way to write to the trail and only one way an
///     entry may be shaped — an ADO.NET writer for producers that hold no DbContext, and an EF
///     interceptor adding rows to the application's own context. Each of these tests is a rule that a
///     new writer silently breaks if it does its own thing.
/// </remarks>
public class AuditEntryPreparerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 14, 30, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static AuditEntryPreparer Create()
        => new(new PatternAuditDetailRedactor(), new HourlyAuditSegmentNaming(), new FixedClock());

    private static AuditEntry Entry(string operation = "Config.Changed") => new()
    {
        SegmentId = string.Empty,
        Operation = operation,
        Category = AuditCategory.Configuration,
        Outcome = AuditOutcome.Success,
    };

    [Fact]
    public void AnUnstampedEntry_GetsTheCurrentTime()
        => Create().Prepare(Entry()).OccurredAt.Should().Be(Now);

    [Fact]
    public void AnEntryThatCarriesItsOwnTime_KeepsIt()
    {
        // A producer recording something that happened a moment ago must not have it restamped, or the
        // entry lands in the wrong segment and the trail says the wrong hour.
        var earlier = Now.AddMinutes(-90);
        var entry = Entry();
        entry.OccurredAt = earlier;

        Create().Prepare(entry).OccurredAt.Should().Be(earlier);
    }

    [Fact]
    public void TheSegmentFollowsTheEntrysOwnTime_NotTheClock()
    {
        var entry = Entry();
        entry.OccurredAt = Now.AddHours(-3);

        Create().Prepare(entry).SegmentId.Should().Be(new HourlyAuditSegmentNaming().SegmentFor(entry.OccurredAt));
    }

    [Fact]
    public void ADetailCarryingAPersonalValue_IsRedacted()
    {
        var entry = Entry();
        entry.Detail = "changed by ada@example.com";

        var prepared = Create().Prepare(entry);

        prepared.Detail.Should().NotContain("ada@example.com");
        prepared.Detail.Should().Contain("[redacted]");
    }

    [Fact]
    public void AnEmptyDetail_IsLeftAlone()
    {
        Create().Prepare(Entry()).Detail.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEntryWithoutAnOperation_IsRefused(string operation)
    {
        // The operation is what anyone searches for when an entry needs explaining. An entry without one
        // is a row nobody can trace back to the code that wrote it.
        var act = () => Create().Prepare(Entry(operation));

        act.Should().Throw<ArgumentException>();
    }
}
