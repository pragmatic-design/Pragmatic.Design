using Pragmatic.Audit.Management.Actions;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Audit.Management.Tests;

/// <summary>
///     The retention operation prunes through the trail the host registered, whichever store implements it: it names only
///     <see cref="IAuditRetention" />. The stores' own pruning is tested where they are (EF Core's in Pragmatic.Audit.EFCore.Tests, the engines'
///     in AuditTrailOnTwoEnginesTests).
/// </summary>
public class PruneAuditTrailTests
{
    [Fact]
    public async Task ThePrune_AsksTheRegisteredRetention_ForTheWindow_AndReportsTheGap()
    {
        var retention = new RecordingRetention(new PrunedRange("seg-1", "seg-4", DateTimeOffset.UnixEpoch, [1, 2, 3]));
        var prune = new PruneAuditTrail { RetentionDays = 30 };
        prune.SetDependencies(retention);

        var result = await prune.Execute();

        result.IsSuccess.Should().BeTrue();
        retention.AskedFor.Should().Be(TimeSpan.FromDays(30));
        result.Value.Should().Be(new PruneAuditTrailResult(true, "seg-1", "seg-4"));
    }

    /// <summary>The control: a window of zero days would discard everything sealed, and is refused before the store is asked.</summary>
    [Fact]
    public async Task AWindowThatIsNotPositive_IsRefused_AndTheStoreIsNotAsked()
    {
        var retention = new RecordingRetention(null);
        var prune = new PruneAuditTrail { RetentionDays = 0 };
        prune.SetDependencies(retention);

        var result = await prune.Execute();

        result.IsFailure.Should().BeTrue();
        retention.AskedFor.Should().BeNull();
    }

    private sealed class RecordingRetention(PrunedRange? gap) : IAuditRetention
    {
        public TimeSpan? AskedFor { get; private set; }

        public Task<PrunedRange?> PruneOlderThanAsync(TimeSpan retention, CancellationToken ct = default)
        {
            AskedFor = retention;
            return Task.FromResult(gap);
        }
    }
}
