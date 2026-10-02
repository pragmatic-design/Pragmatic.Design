using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Audit.EFCore.Tests;

/// <summary>
///     Whether an entry written inside the caller's transaction really shares its fate.
/// </summary>
/// <remarks>
///     This is the guarantee two producers depend on — the configuration store and the persistence
///     interceptor. The rollback test is the one that matters: if the entry survived a rolled-back change, the trail
///     would record something that never happened.
/// </remarks>
public class TransactionalAuditTrailTests
{
    [Fact]
    public async Task AnEntryWrittenInARolledBackTransaction_DoesNotSurvive()
    {
        using var f = new AuditTrailFixture();

        var transaction = (SqliteTransaction)await f.Connection.BeginTransactionAsync();
        await using (transaction.ConfigureAwait(false))
        {
            await f.Trail.RecordAsync(f.Entry("Config.Changed"), transaction);
            await transaction.RollbackAsync();
        }

        f.Db.ChangeTracker.Clear();
        (await f.Db.Entries.CountAsync()).Should().Be(0,
            "a trail that keeps the record of a change that was undone describes a system that does not exist");
    }

    [Fact]
    public async Task AnEntryWrittenInACommittedTransaction_Survives()
    {
        using var f = new AuditTrailFixture();

        var transaction = (SqliteTransaction)await f.Connection.BeginTransactionAsync();
        await using (transaction.ConfigureAwait(false))
        {
            await f.Trail.RecordAsync(f.Entry("Config.Changed"), transaction);
            await transaction.CommitAsync();
        }

        f.Db.ChangeTracker.Clear();
        (await f.Db.Entries.SingleAsync()).Operation.Should().Be("Config.Changed");
    }

    [Fact]
    public async Task ATransactionOnAnotherConnection_ThrowsRatherThanWritingOutsideIt()
    {
        // The failure this guards against is the quiet one: EF would have saved on its own connection,
        // producing an audit row that commits independently of the change it claims to record.
        using var f = new AuditTrailFixture();

        var other = new SqliteConnection("Filename=:memory:");
        await using (other.ConfigureAwait(false))
        {
            await other.OpenAsync();
            var foreign = (SqliteTransaction)await other.BeginTransactionAsync();
            await using (foreign.ConfigureAwait(false))
            {
                var act = async () => await f.Trail.RecordAsync(f.Entry(), foreign);

                (await act.Should().ThrowAsync<InvalidOperationException>())
                    .WithMessage("*same connection*");
            }
        }

        f.Db.ChangeTracker.Clear();
        (await f.Db.Entries.CountAsync()).Should().Be(0, "the refusal must not have written anything");
    }

    [Fact]
    public async Task RedactionAppliesOnTheEnlistedPathToo()
    {
        // Easy to lose when a second write path appears: the promise that an entry holds no personal
        // value has to hold on both, or the producers being migrated become the ones that leak.
        using var f = new AuditTrailFixture();

        var transaction = (SqliteTransaction)await f.Connection.BeginTransactionAsync();
        await using (transaction.ConfigureAwait(false))
        {
            var entry = f.Entry();
            entry.Detail = "changed by ada@example.com";

            await f.Trail.RecordAsync(entry, transaction);
            await transaction.CommitAsync();
        }

        f.Db.ChangeTracker.Clear();
        var stored = await f.Db.Entries.SingleAsync();
        stored.Detail.Should().NotContain("ada@example.com");
        stored.Detail.Should().Contain("[redacted]");
    }

    [Fact]
    public async Task TwoEntriesInOneTransaction_BothLandOrNeitherDoes()
    {
        using var f = new AuditTrailFixture();

        var transaction = (SqliteTransaction)await f.Connection.BeginTransactionAsync();
        await using (transaction.ConfigureAwait(false))
        {
            await f.Trail.RecordAsync(f.Entry("First"), transaction);
            await f.Trail.RecordAsync(f.Entry("Second"), transaction);
            await transaction.RollbackAsync();
        }

        f.Db.ChangeTracker.Clear();
        (await f.Db.Entries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TheNonTransactionalPathStillWorks()
    {
        // Both contracts resolve to one instance; a change to the enlisted path must not disturb the
        // ordinary one, which is what nearly every caller uses.
        using var f = new AuditTrailFixture();

        await f.Trail.RecordAsync(f.Entry("Plain"));

        f.Db.ChangeTracker.Clear();
        (await f.Db.Entries.SingleAsync()).Operation.Should().Be("Plain");
    }
}
