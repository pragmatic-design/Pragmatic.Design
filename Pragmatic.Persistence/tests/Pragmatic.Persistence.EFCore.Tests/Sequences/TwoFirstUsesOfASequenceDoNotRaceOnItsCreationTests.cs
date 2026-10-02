using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pragmatic.Persistence.EFCore.Sequences;
using Pragmatic.Persistence.EFCore.Tests.Integration;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Sequences;

/// <summary>
///     The <b>first</b> use of a <c>[GeneratedValue] {SEQ:N}</c> sequence is safe under
///     concurrency, which is exactly when an application starting under load asks for it.
/// </summary>
/// <remarks>
///     <para>
///         <c>CREATE SEQUENCE IF NOT EXISTS</c> is <b>not atomic</b>: PostgreSQL checks the catalogue
///         against a snapshot and then inserts, so two sessions reaching it for a sequence that does not
///         exist yet both pass the check and the loser inserts a row that is already there —
///         <c>23505</c> on <c>pg_class_relname_nsp_index</c>. Measured on Casework: three requests
///         creating a case at once, and the error surfaced as the failure of the insert that needed the
///         number. Handing values out is concurrency-safe; creating the sequence was not.
///     </para>
///     <para>
///         ⚠️ <b>These tests force the race instead of hoping for it.</b> N tasks racing is the shape
///         that produced the trace, but it is a probability, and a test that is only sometimes red is
///         not a signal. The first two cases make it deterministic: one session creates the sequence
///         inside an <b>open transaction</b>, so the row exists in the catalogue and is invisible to
///         everybody else — the second caller's <c>CREATE</c> then blocks on the uncommitted row and is
///         released, as a duplicate, by the commit. That is the same collision the three requests had,
///         with the timing decided rather than drawn.
///     </para>
///     <para>
///         PostgreSQL rather than SQLite, because the defect is in what this provider's
///         <c>IF NOT EXISTS</c> does not promise. SQLite has no sequences (the provider emulates them
///         with a counter table) and SQL Server has the same shape with <c>2714</c> — asserting it there
///         would mean a second container for the same race.
///     </para>
/// </remarks>
[Trait("Category", "Testcontainers")]
public sealed class TwoFirstUsesOfASequenceDoNotRaceOnItsCreationTests(PostgresContainerFixture fixture)
    : IAsyncLifetime, IClassFixture<PostgresContainerFixture>
{
    /// <summary>A database of this class's own inside the shared container.</summary>
    private readonly string _database = "seqrace_" + Guid.NewGuid().ToString("N");

    private string _connectionString = null!;
    private ProbeContext _owner = null!;

    public async Task InitializeAsync()
    {
        _connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = _database
        }.ConnectionString;

        // Creates the database as well as the (empty) schema, which is why no CREATE DATABASE is needed.
        _owner = Context();
        await _owner.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await _owner.Database.EnsureDeletedAsync().ConfigureAwait(false);
        await _owner.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     The setpoint: while one session is still creating the sequence, the next caller gets a
    ///     number instead of a duplicate key.
    /// </summary>
    [Fact]
    public async Task WhileAnotherSessionIsCreatingIt_TheSecondCallerGetsANumber()
    {
        const string sequence = "Case_Number_seq";

        using var creating = Context();
        using var racing = Context();

        // Inside a transaction, so the sequence exists in this session's catalogue and in nobody
        // else's: the provider enlists in the ambient transaction rather than opening its own.
        var transaction = await creating.Database.BeginTransactionAsync();
        var firstValue = await SequenceValueProvider.NextAsync(creating, sequence);

        var second = Task.Run(() => SequenceValueProvider.NextAsync(racing, sequence));
        await WaitUntilSomebodyIsBlockedAsync();

        await transaction.CommitAsync();

        // ConfigureAwait(true) and not (false): CA2007 wants one on a Task from Task.Run, and
        // xUnit1030 refuses (false) in a test method because it bypasses the parallelization limits.
        var secondValue = await second.ConfigureAwait(true);

        secondValue.Should().NotBe(firstValue,
            "the second caller was handed the sequence's next value, not the same one");
        secondValue.Should().BeGreaterThan(0);
    }

    /// <summary>
    ///     The same race with the second caller inside a transaction of its own, which has to survive
    ///     it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the half a bare <c>catch</c> gets wrong. In PostgreSQL a failed statement aborts
    ///     the whole transaction it ran in, so swallowing the duplicate and carrying on would leave the
    ///     caller's transaction in <c>25P02</c> — "current transaction is aborted" — and the insert that
    ///     wanted the number would fail anyway, with a worse message than before. The create has to be
    ///     attempted inside a savepoint, and this case is the only one that can tell.
    /// </remarks>
    [Fact]
    public async Task WhenTheSecondCallerIsInATransaction_ThatTransactionStaysUsable()
    {
        const string sequence = "Invoice_Number_seq";

        using var creating = Context();
        using var racing = Context();

        var firstTransaction = await creating.Database.BeginTransactionAsync();
        await SequenceValueProvider.NextAsync(creating, sequence);

        var secondTransaction = await racing.Database.BeginTransactionAsync();
        var second = Task.Run(() => SequenceValueProvider.NextAsync(racing, sequence));
        await WaitUntilSomebodyIsBlockedAsync();

        await firstTransaction.CommitAsync();
        await second.ConfigureAwait(true);

        // The assertion: the transaction the caller opened is still alive. A statement, then a commit —
        // both of which an aborted transaction refuses.
        var stillUsable = await SequenceValueProvider.NextAsync(racing, sequence);
        await secondTransaction.CommitAsync();

        stillUsable.Should().BeGreaterThan(0,
            "the caller's transaction was not aborted by the create that lost the race");
    }

    /// <summary>
    ///     And the shape the defect arrived in: several first uses at once, each with its own
    ///     connection, all of them getting a number of their own.
    /// </summary>
    /// <remarks>
    ///     Its red is a probability, which is why the two cases above exist; it is here because it is
    ///     what an application does, and because a fix that only covered the choreographed collision
    ///     would pass those two and fail this one.
    /// </remarks>
    [Fact]
    public async Task EightConcurrentFirstUses_EachGetADistinctNumber()
    {
        const string sequence = "Order_Number_seq";
        const int callers = 8;

        var contexts = Enumerable.Range(0, callers).Select(_ => Context()).ToList();
        var gate = new TaskCompletionSource();

        try
        {
            // The connection is opened before the gate so that connecting is not what staggers them:
            // what has to overlap is the CREATE, not the handshake.
            foreach (var context in contexts)
                await context.Database.OpenConnectionAsync();

            var racing = contexts
                .Select(context => Task.Run(async () =>
                {
                    await gate.Task.ConfigureAwait(true);
                    return await SequenceValueProvider.NextAsync(context, sequence).ConfigureAwait(true);
                }))
                .ToList();

            gate.SetResult();
            var values = await Task.WhenAll(racing);

            values.Should().HaveCount(callers);
            values.Distinct().Should().HaveCount(callers,
                "a sequence never hands the same number to two callers");
        }
        finally
        {
            foreach (var context in contexts)
                context.Dispose();
        }
    }

    /// <summary>
    ///     The control: when the duplicate key does <b>not</b> mean "the sequence exists", it is still
    ///     an error.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ "Treat the failure as meaning the sequence exists" is satisfied by swallowing
    ///         everything, and that version would be worse than the race: the caller would
    ///         get a different error one statement later, or none. So the post-condition is checked —
    ///         and checked for a <b>sequence</b>, not for any relation of that name.
    ///     </para>
    ///     <para>
    ///         This case is the same race against a session creating a <b>table</b> of the name:
    ///         <c>pg_class</c> is one namespace for both, so the create still loses with <c>23505</c>,
    ///         and the sequence still does not exist. An existence check that asked
    ///         <c>to_regclass</c> alone — the first version of it — would read the table as the
    ///         sequence, swallow the error, and hand the caller a <c>42809</c> from <c>nextval</c>
    ///         instead.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task WhenTheNameWasTakenByATableInstead_TheRaceIsStillAnError()
    {
        const string taken = "Contended_Name_seq";

        using var creating = Context();
        using var racing = Context();

        var transaction = await creating.Database.BeginTransactionAsync();
        await creating.Database.ExecuteSqlRawAsync($"create table \"{taken}\" (id int)");

        var second = Task.Run(() => SequenceValueProvider.NextAsync(racing, taken));
        await WaitUntilSomebodyIsBlockedAsync();

        await transaction.CommitAsync();

        var failed = await Assert.ThrowsAsync<PostgresException>(() => second);

        failed.SqlState.Should().Be("23505",
            "the error the create raised, and not one from a statement that ran after it was swallowed");
    }

    private ProbeContext Context()
        => new(new DbContextOptionsBuilder().UseNpgsql(_connectionString).Options);

    /// <summary>
    ///     Waits until a session on this database is waiting on a lock — which is what the racing
    ///     caller does while the uncommitted catalogue row is in its way.
    /// </summary>
    /// <remarks>
    ///     Read from <c>pg_stat_activity</c> rather than slept over: a delay long enough to be reliable
    ///     is paid on every run, and one short enough to be cheap lets timing decide the outcome. The
    ///     failure says what never happened.
    /// </remarks>
    private async Task WaitUntilSomebodyIsBlockedAsync(int timeoutSeconds = 30)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var command = new NpgsqlCommand(
                "select count(*) from pg_stat_activity where wait_event_type = 'Lock' and datname = @db",
                connection);
            command.Parameters.AddWithValue("db", _database);

            var waiting = await command.ExecuteScalarAsync().ConfigureAwait(false);
            if (Convert.ToInt64(waiting) > 0)
                return;

            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new Xunit.Sdk.XunitException(
            $"waited {timeoutSeconds}s and no session was ever waiting on a lock: the second caller "
            + "never reached the CREATE, so this test is not measuring the race it describes");
    }

    private sealed class ProbeContext(DbContextOptions options) : DbContext(options);
}
