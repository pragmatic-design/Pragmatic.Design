using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Migrations.Runner;

namespace Pragmatic.Migrations.Core.Tests.Runner;

/// <summary>
///     Tests the fail-safe contract of <see cref="FallbackLeaderElection"/>: an inner-election
///     failure must NEVER be interpreted as leadership, otherwise every pod migrates at once
///     (split-brain). Cancellation must propagate cleanly rather than be masked into a decision.
/// </summary>
public class FallbackLeaderElectionTests
{
    private static FallbackLeaderElection Wrap(IMigrationLeaderElection inner)
        => new(inner, NullLogger.Instance);

    [Fact]
    public async Task TryBecomeLeader_InnerReturnsTrue_ReturnsTrue()
    {
        var sut = Wrap(new FakeElection { BecomeResult = true });

        (await sut.TryBecomeLeaderAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task TryBecomeLeader_InnerReturnsFalse_ReturnsFalse()
    {
        var sut = Wrap(new FakeElection { BecomeResult = false });

        (await sut.TryBecomeLeaderAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task TryBecomeLeader_InnerThrows_DefaultsToNotLeader()
    {
        // The critical anti-split-brain guarantee: a transient DB failure must yield false.
        var sut = Wrap(new FakeElection { BecomeException = new InvalidOperationException("db down") });

        (await sut.TryBecomeLeaderAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task TryBecomeLeader_Cancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = Wrap(new FakeElection { BecomeException = new OperationCanceledException(cts.Token) });

        var act = () => sut.TryBecomeLeaderAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReleaseLeadership_InnerThrows_DoesNotPropagate()
    {
        var sut = Wrap(new FakeElection { ReleaseException = new InvalidOperationException("release failed") });

        var act = () => sut.ReleaseLeadershipAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WaitForLeaderCompletion_InnerThrows_DoesNotPropagate()
    {
        // A follower that cannot poll simply stops waiting; it must never crash or migrate itself.
        var sut = Wrap(new FakeElection { WaitException = new InvalidOperationException("poll failed") });

        var act = () => sut.WaitForLeaderCompletionAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WaitForLeaderCompletion_Cancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = Wrap(new FakeElection { WaitException = new OperationCanceledException(cts.Token) });

        var act = () => sut.WaitForLeaderCompletionAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FakeElection : IMigrationLeaderElection
    {
        public bool BecomeResult { get; init; }
        public Exception? BecomeException { get; init; }
        public Exception? ReleaseException { get; init; }
        public Exception? WaitException { get; init; }

        public Task<bool> TryBecomeLeaderAsync(CancellationToken ct = default)
            => BecomeException is not null ? Task.FromException<bool>(BecomeException) : Task.FromResult(BecomeResult);

        public Task ReleaseLeadershipAsync(CancellationToken ct = default)
            => ReleaseException is not null ? Task.FromException(ReleaseException) : Task.CompletedTask;

        public Task WaitForLeaderCompletionAsync(CancellationToken ct = default)
            => WaitException is not null ? Task.FromException(WaitException) : Task.CompletedTask;
    }
}
