#pragma warning disable CA2007

using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pragmatic.Migrations.Runner;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Migrations.Tests.Provider;

/// <summary>
///     On an empty database, an instance whose lock table was created by another one at the same
///     moment still takes part in the election.
/// </summary>
/// <remarks>
///     <para>
///         Two instances starting together both run <c>CREATE TABLE IF NOT EXISTS "__PragmaticLock"</c>.
///         PostgreSQL's <c>IF NOT EXISTS</c> is not safe against a concurrent create: the second waits for the
///         first's transaction and then fails with <c>23505</c> on <c>pg_type_typname_nsp_index</c>. The
///         election threw, the fail-safe wrapper made the instance a follower, and it never competed. Seen on
///         Warehouse, two Stock instances starting on a new database.
///     </para>
///     <para>
///         Choreographed, not hoped for: another session creates the table inside a transaction it holds
///         open; the election is started and seen waiting on it in <c>pg_stat_activity</c>; only then does
///         the other session commit.
///     </para>
/// </remarks>
public sealed class TwoInstancesElectOneLeaderOnAnEmptyDatabaseTests(PostgreSqlContainerFixture fixture)
    : IClassFixture<PostgreSqlContainerFixture>, IAsyncLifetime
{
    private readonly string _database = "election_" + Guid.NewGuid().ToString("N");

    public async Task InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(fixture.ConnectionString);
        await admin.OpenAsync();
        await using var create = admin.CreateCommand();
        // The name is a GUID this class made, never user input.
        create.CommandText = $"CREATE DATABASE \"{_database}\";";
        await create.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TheLockTableCreatedByAnotherAtTheSameMoment_StillElectsThisOne()
    {
        await using var other = await OpenAsync();
        await using var otherTransaction = await other.BeginTransactionAsync();
        await ExecuteAsync(other, """
            CREATE TABLE IF NOT EXISTS "__PragmaticLock" (
                "LockName"    VARCHAR(128) NOT NULL PRIMARY KEY,
                "HolderId"    VARCHAR(64)  NULL,
                "AcquiredAt"  TIMESTAMPTZ  NULL,
                "ExpiresAt"   TIMESTAMPTZ  NULL
            )
            """, otherTransaction);

        var election = Election("this-instance");
        var elected = election.TryBecomeLeaderAsync();

        await UntilWaitingOnALockAsync();
        await otherTransaction.CommitAsync();

        (await elected).Should().BeTrue("the table exists now, and nobody holds the lock");
        await election.ReleaseLeadershipAsync();
    }

    /// <summary>The control: with the table already there, a second instance is a follower, not a second leader.</summary>
    [Fact]
    public async Task WithTheTableThere_TheSecondInstance_IsAFollower()
    {
        var first = Election("first");
        var second = Election("second");

        (await first.TryBecomeLeaderAsync()).Should().BeTrue();
        (await second.TryBecomeLeaderAsync()).Should().BeFalse();

        await first.ReleaseLeadershipAsync();
    }

    private DatabaseLeaderElection Election(string hostId) => new(
        async () => await OpenAsync(), "PostgreSql", NullLogger<DatabaseLeaderElection>.Instance, hostId: hostId);

    /// <summary>Returns once a session of this database is waiting on a lock — the election, blocked by the other create.</summary>
    private async Task UntilWaitingOnALockAsync()
    {
        await using var observer = await OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var probe = observer.CreateCommand();
            probe.CommandText = """
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock' AND pid <> pg_backend_pid()
                """;
            if ((long)(await probe.ExecuteScalarAsync())! > 0)
                return;

            await Task.Delay(20);
        }

        throw new TimeoutException("the election never waited on the other session's create");
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = _database }.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync();
    }
}
