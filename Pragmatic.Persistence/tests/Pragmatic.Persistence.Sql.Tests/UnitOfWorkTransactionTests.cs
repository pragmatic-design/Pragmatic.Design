using Pragmatic.Actions.Commit;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Pragmatic.Result.Http;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The unit of work's transactions (Pragmatic's <c>ITransaction</c>, the savepoints), what it forgets (<c>Detach</c>, <c>DiscardChanges</c>),
///     where its events go (a covering batch of <c>CommitScope</c>), and the rows it refuses to write: one the caller may not read. Five databases.
/// </summary>
[Collection("pragmatic databases")]
public class UnitOfWorkTransactionTests(PragmaticDatabases databases)
{
    private static readonly DateTimeOffset Now = UnitOfWorkTests.Now;
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private static SqlUnitOfWork UnitOfWork(SqlSession session, RecordingDispatcher? dispatcher = null)
        => new(session, new FixedClock(Now), new TestUser("anna"), new TenantScope(), dispatcher: dispatcher);

    private async Task<string> NameAsync(SqlSession session, Guid id) => (await UnitOfWorkTests.RawAsync<Property>(session, _toggle)).Single(p => p.Id == id).Name;

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_rolled_back_transaction_writes_nothing_and_a_savepoint_undoes_what_followed_it(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var unitOfWork = UnitOfWork(session);
        var riva = (await new Property.SqlRepository(unitOfWork).FindAsync(Pragmatic.Specification.Spec<Property>.True)).Single();

        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            Assert.Equal(TransactionState.Active, unitOfWork.State);
            riva.Name = "rolled back";
            await unitOfWork.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        Assert.Equal(TransactionState.RolledBack, unitOfWork.State);
        Assert.Equal("RIVA", await NameAsync(session, riva.Id));

        var other = UnitOfWork(session);
        var again = (await new Property.SqlRepository(other).GetByIdAsync(riva.Id))!;
        await using (var transaction = await other.BeginTransactionAsync())
        {
            again.Name = "kept";
            await other.SaveChangesAsync();
            await other.SavepointAsync("before_second");
            again.Name = "undone";
            await other.SaveChangesAsync();
            await other.RollbackToSavepointAsync("before_second");
            await transaction.CommitAsync();
        }

        Assert.True(((IUnitOfWork)other).IsCommitted); // the contract's default member, from State
        Assert.Equal("kept", await NameAsync(session, riva.Id));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_detached_or_discarded_entity_is_not_saved(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var unitOfWork = UnitOfWork(session);
        IRepository<Property> properties = new Property.SqlRepository(unitOfWork);
        var riva = (await properties.FindAsync(Pragmatic.Specification.Spec<Property>.True)).Single();
        riva.Name = "detached";
        unitOfWork.Detach(riva);
        Assert.Equal(0, await unitOfWork.SaveChangesAsync());

        var reread = (await properties.GetByIdAsync(riva.Id))!;
        Assert.NotSame(riva, reread); // forgotten: the read builds a new one
        reread.Name = "discarded";
        properties.Add(new Property { Name = "NEVE" });
        unitOfWork.DiscardChanges();
        Assert.Equal(0, await unitOfWork.SaveChangesAsync());
        Assert.Equal("RIVA", await NameAsync(session, riva.Id));
        Assert.DoesNotContain(await UnitOfWorkTests.RawAsync<Property>(session, _toggle), p => p.Name == "NEVE");
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_events_go_to_the_covering_batch_when_an_invoker_owns_the_commit(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var dispatcher = new RecordingDispatcher();
        var unitOfWork = UnitOfWork(session, dispatcher);
        using (var ownership = CommitScope.Claim(unitOfWork, CommitMode.Once))
        {
            new Amenity.SqlRepository(unitOfWork).Add(new Amenity { Name = "Spa" });
            await unitOfWork.SaveChangesAsync();
            Assert.Empty(dispatcher.Dispatched); // the owner flushes them after its commit
            Assert.Equal(["Spa"], ownership.Batch!.GetEventsByType()[typeof(AmenityChanged)].Cast<AmenityChanged>().Select(e => e.Name));
        }

        new Amenity.SqlRepository(unitOfWork).Add(new Amenity { Name = "Gym" });
        await unitOfWork.SaveChangesAsync();
        Assert.Equal(["Gym"], dispatcher.Dispatched.OfType<AmenityChanged>().Select(e => e.Name)); // no owner: dispatched here, after the commit
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task ClosesEfGap_a_row_staged_without_a_read_is_written_only_when_the_caller_may_read_it(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var rows = await UnitOfWorkTests.RawAsync<Property>(session, _toggle);
        var (riva, alpe) = (rows.Single(p => p.Name == "RIVA"), rows.Single(p => p.Name == "ALPE")); // ALPE is another tenant's

        // On EF Core, Update(e) of an entity built or read elsewhere writes it by key, whoever's row it is.
        var unitOfWork = UnitOfWork(session);
        alpe.Name = "ALPE taken";
        new Property.SqlRepository(unitOfWork).Update(alpe);
        var refused = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => unitOfWork.SaveChangesAsync());
        Assert.Equal(alpe.Id.ToString(), Assert.IsType<NotFoundError>(refused.Error).EntityId);
        Assert.Equal("ALPE", await NameAsync(session, alpe.Id));

        var bruno = (await UnitOfWorkTests.RawAsync<Reservation>(session, _toggle)).Single(r => r.OwnerId == "bruno"); // another owner's
        var other = UnitOfWork(session);
        new Reservation.SqlRepository(other).Remove(bruno);
        await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => other.SaveChangesAsync());
        Assert.False((await UnitOfWorkTests.RawAsync<Reservation>(session, _toggle)).Single(r => r.Id == bruno.Id).IsDeleted);

        // The caller's own row, never read through the unit of work: written whole.
        var own = UnitOfWork(session);
        riva.Name = "RIVA updated";
        new Property.SqlRepository(own).Update(riva);
        Assert.Equal(1, await own.SaveChangesAsync());
        Assert.Equal("RIVA updated", await NameAsync(session, riva.Id));
    }
}
