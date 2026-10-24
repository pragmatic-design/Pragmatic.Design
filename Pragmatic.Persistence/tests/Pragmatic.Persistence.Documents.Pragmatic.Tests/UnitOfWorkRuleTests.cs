using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Documents.Pragmatic;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result.Database;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     What a documents save refuses and what it holds together: a stale document (<c>[ConcurrencyAware]</c>'s <c>_v</c>, Cosmos DB's etag) is
///     Pragmatic's <see cref="ConcurrencyError" />, a taken key a conflict; on MongoDB (a replica set) the save is one transaction, on Cosmos DB
///     it is not (persistence/docs/write-surface.md). No savepoints; a transient failure is run again from a cleared unit of work.
/// </summary>
[Collection("document servers")]
public class UnitOfWorkRuleTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_second_of_two_writers_of_a_document_meets_a_concurrency_error(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await UnitOfWorkTests.AnnaAsync(servers, store, _toggle);
        var seeding = UnitOfWorkTests.UnitOfWork(session);
        var wifi = new Amenity { Name = "WiFi" };
        new Amenity.DocRepository(seeding).Add(wifi);
        await seeding.SaveChangesAsync();

        var (first, second) = (UnitOfWorkTests.UnitOfWork(session), UnitOfWorkTests.UnitOfWork(session));
        var mine = (await new Amenity.DocRepository(first).GetByIdAsync(wifi.Id))!;
        var theirs = (await new Amenity.DocRepository(second).GetByIdAsync(wifi.Id))!;
        Assert.Equal(mine.RowVersion, theirs.RowVersion);

        mine.Name = "WiFi 6";
        await first.SaveChangesAsync();
        Assert.Equal(theirs.RowVersion + 1, mine.RowVersion); // advanced, and known to the writer: it can write again
        mine.Name = "WiFi 7";
        await first.SaveChangesAsync();

        theirs.Name = "Fibre";
        var stale = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => second.SaveChangesAsync());
        var conflict = Assert.IsType<ConcurrencyError>(stale.Error);
        Assert.Equal(("CONCURRENCY_CONFLICT", 409, "Amenity"), (conflict.Code, conflict.StatusCode, conflict.EntityTypeName));
        Assert.Equal(("WiFi 7", 2UL), (await UnitOfWorkTests.AmenitiesAsync(session, _toggle)).Where(a => a.Id == wifi.Id).Select(a => (a.Name, a.RowVersion)).Single());

        // A delete at a stale copy is refused too; at the current one it goes.
        var reader = UnitOfWorkTests.UnitOfWork(session);
        var current = (await new Amenity.DocRepository(reader).GetByIdAsync(wifi.Id))!;
        second.DiscardChanges();
        new Amenity.DocRepository(second).Remove(theirs);
        await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => second.SaveChangesAsync());
        new Amenity.DocRepository(reader).Remove(current);
        Assert.Equal(1, await reader.SaveChangesAsync());
        Assert.DoesNotContain(await UnitOfWorkTests.AmenitiesAsync(session, _toggle), a => a.Id == wifi.Id);
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_taken_key_is_a_conflict(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await UnitOfWorkTests.AnnaAsync(servers, store, _toggle);
        var first = UnitOfWorkTests.UnitOfWork(session);
        var wifi = new Amenity { Name = "WiFi" };
        new Amenity.DocRepository(first).Add(wifi);
        await first.SaveChangesAsync();

        var second = UnitOfWorkTests.UnitOfWork(session);
        new Amenity.DocRepository(second).Add(new Amenity { PersistenceId = wifi.Id, Name = "WiFi again" });
        var broken = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => second.SaveChangesAsync());
        var duplicate = Assert.IsType<DbConflictError>(broken.Error);
        Assert.Equal(("DB_CONFLICT", 409, "Amenity"), (duplicate.Code, duplicate.StatusCode, duplicate.EntityType));
        // MongoDB names the index and the field (the key's: PersistenceId); Cosmos DB names neither.
        Assert.Equal(store == Store.Mongo ? ("PersistenceId", "_id_") : (null, null), (duplicate.FieldName, duplicate.ConstraintName));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_failing_phase_undoes_the_save_on_MongoDB_and_not_on_Cosmos_DB(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await UnitOfWorkTests.AnnaAsync(servers, store, _toggle);
        var unitOfWork = UnitOfWorkTests.UnitOfWork(session, phases: new JournalPhase(fail: true));
        var spa = new Amenity { Name = "Spa" };
        new Amenity.DocRepository(unitOfWork).Add(spa);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.SaveChangesAsync());

        var stored = (await UnitOfWorkTests.AmenitiesAsync(session, _toggle)).Select(a => a.Name).Order(StringComparer.Ordinal).ToList();
        if (store == Store.Mongo)
            Assert.Empty(stored); // one transaction: the entity and the journal aborted together
        else
            Assert.Equal(["Spa", "journal:Amenity:Added"], stored); // not atomic across documents: what was written stays (declared)
    }

    [Fact]
    public async Task A_transaction_of_the_caller_holds_two_saves_and_rolls_back()
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await UnitOfWorkTests.AnnaAsync(servers, Store.Mongo, _toggle);
        var unitOfWork = UnitOfWorkTests.UnitOfWork(session);
        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            new Amenity.DocRepository(unitOfWork).Add(new Amenity { Name = "Gym" });
            await unitOfWork.SaveChangesAsync();
            new Amenity.DocRepository(unitOfWork).Add(new Amenity { Name = "Bar" });
            await unitOfWork.SaveChangesAsync();
            Assert.Equal(Pragmatic.Persistence.Repository.TransactionState.Active, unitOfWork.State);
            await transaction.RollbackAsync();
        }

        Assert.Equal(Pragmatic.Persistence.Repository.TransactionState.RolledBack, unitOfWork.State);
        Assert.Empty(await UnitOfWorkTests.AmenitiesAsync(session, _toggle));
        await Assert.ThrowsAsync<NotSupportedException>(() => unitOfWork.SavepointAsync("before"));
    }

    [Fact]
    public async Task Cosmos_DB_opens_no_transaction()
    {
        var unitOfWork = UnitOfWorkTests.UnitOfWork(servers.Session(Store.Cosmos));
        await Assert.ThrowsAsync<NotSupportedException>(() => unitOfWork.BeginTransactionAsync());
    }

    [Fact]
    public async Task A_transient_failure_runs_again_from_a_cleared_unit_of_work()
    {
        var unitOfWork = UnitOfWorkTests.UnitOfWork(servers.Session(Store.Mongo));
        var attempts = 0;
        var tracked = await unitOfWork.ExecuteAsync(ct =>
        {
            attempts++;
            var count = unitOfWork.Entries.Count;
            unitOfWork.Add(new Amenity { Name = "Spa" });
            if (attempts == 1)
                throw new MongoCommandException(112, "WriteConflict", "a write conflict", ["TransientTransactionError"]);
            return Task.FromResult(count);
        });

        Assert.Equal((2, 0), (attempts, tracked)); // the second attempt started from nothing tracked
        await Assert.ThrowsAsync<MongoCommandException>(() => unitOfWork.ExecuteAsync<int>(_ => throw new MongoCommandException(2, "BadValue", "not transient")));
    }
}
