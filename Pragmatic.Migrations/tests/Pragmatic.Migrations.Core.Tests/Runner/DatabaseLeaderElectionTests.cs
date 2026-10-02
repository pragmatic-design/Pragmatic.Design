using System.Data.Common;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Migrations.Runner;

namespace Pragmatic.Migrations.Core.Tests.Runner;

public sealed class DatabaseLeaderElectionTests : IDisposable
{
    // Shared in-memory SQLite DB (connection string with shared cache)
    private const string SharedCs = "Data Source=LeaderElectionTest;Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;

    public DatabaseLeaderElectionTests()
    {
        // SQLite in-memory DBs are destroyed when last connection closes.
        // Keep one connection alive for the entire test.
        _keepAlive = new SqliteConnection(SharedCs);
        _keepAlive.Open();
    }

    private static Task<DbConnection> CreateConnection()
    {
        var conn = new SqliteConnection(SharedCs);
        conn.Open();
        return Task.FromResult<DbConnection>(conn);
    }

    private static DatabaseLeaderElection CreateElection(string? hostId = null) => new(
        CreateConnection,
        "Sqlite",
        NullLogger<DatabaseLeaderElection>.Instance,
        lockTimeout: TimeSpan.FromMinutes(5),
        hostId: hostId);

    [Fact]
    public async Task FirstInstance_BecomesLeader()
    {
        var election = CreateElection("host-1");
        var isLeader = await election.TryBecomeLeaderAsync();

        isLeader.Should().BeTrue();

        await election.ReleaseLeadershipAsync();
    }

    [Fact]
    public async Task SecondInstance_WaitsForLeader()
    {
        var leader = CreateElection("leader");
        var follower = CreateElection("follower");

        (await leader.TryBecomeLeaderAsync()).Should().BeTrue();
        (await follower.TryBecomeLeaderAsync()).Should().BeFalse();

        await leader.ReleaseLeadershipAsync();
    }

    [Fact]
    public async Task AfterRelease_NextInstanceBecomesLeader()
    {
        var first = CreateElection("first");
        var second = CreateElection("second");

        (await first.TryBecomeLeaderAsync()).Should().BeTrue();
        await first.ReleaseLeadershipAsync();

        (await second.TryBecomeLeaderAsync()).Should().BeTrue();
        await second.ReleaseLeadershipAsync();
    }

    [Fact]
    public async Task WaitForCompletion_ReturnsAfterRelease()
    {
        var leader = CreateElection("leader");
        var follower = CreateElection("follower");

        (await leader.TryBecomeLeaderAsync()).Should().BeTrue();
        (await follower.TryBecomeLeaderAsync()).Should().BeFalse();

        // Follower waits, leader releases after a delay
        var waitTask = follower.WaitForLeaderCompletionAsync();

        await Task.Delay(100);
        await leader.ReleaseLeadershipAsync();

        // Wait should complete within a few poll cycles
        var completed = await Task.WhenAny(waitTask, Task.Delay(5000));
        completed.Should().Be(waitTask, "follower should detect leader completion");
    }

    [Fact]
    public async Task ExpiredLock_CanBeReclaimed()
    {
        // Use a very short timeout
        var dying = new DatabaseLeaderElection(
            CreateConnection, "Sqlite",
            NullLogger<DatabaseLeaderElection>.Instance,
            lockTimeout: TimeSpan.FromSeconds(1),
            hostId: "dying");

        (await dying.TryBecomeLeaderAsync()).Should().BeTrue();

        // Don't release — simulate crash. Wait for expiry.
        await Task.Delay(1500);

        var successor = CreateElection("successor");
        (await successor.TryBecomeLeaderAsync()).Should().BeTrue();
        await successor.ReleaseLeadershipAsync();
    }

    [Fact]
    public async Task SameHost_CanReclaimOwnLock()
    {
        var host = CreateElection("same-host");
        (await host.TryBecomeLeaderAsync()).Should().BeTrue();

        // Same host tries again — should succeed (idempotent)
        var host2 = CreateElection("same-host");
        (await host2.TryBecomeLeaderAsync()).Should().BeTrue();

        await host2.ReleaseLeadershipAsync();
    }

    [Fact]
    public async Task LockTable_CreatedIdempotently()
    {
        var e1 = CreateElection("a");
        var e2 = CreateElection("b");

        // Both create table — no error
        (await e1.TryBecomeLeaderAsync()).Should().BeTrue();
        await e1.ReleaseLeadershipAsync();

        (await e2.TryBecomeLeaderAsync()).Should().BeTrue();
        await e2.ReleaseLeadershipAsync();
    }

    public void Dispose() => _keepAlive.Dispose();
}
