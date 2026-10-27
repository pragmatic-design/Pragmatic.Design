using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     <c>IBulkRepository</c> on the documents engine, on MongoDB and Cosmos DB, as on the Sql engine (<c>BulkWritesTests</c> there). The update is
///     compiled where it is called: one command on MongoDB (a pipeline when a value is computed from the document), one patch per document on
///     Cosmos DB, over the documents the specification selects among the caller's (anna, in acme, holding desk-north). A <c>[SoftDelete]</c>
///     entity's delete marks its documents as the unit of work marks one, and deletes them inside <c>SoftDeleteScope.Suspend</c>. The insert and
///     the upsert are the save's phases applied to the documents alone; the upsert never writes over a document the caller may not read.
/// </summary>
[Collection("document servers")]
public class BulkWritesTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task An_update_sets_a_captured_value_and_a_computed_one_on_the_callers_documents(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await UnitOfWorkTests.AnnaAsync(servers, store, _toggle);
        IBulkRepository<Review> reviews = new Review.DocRepository(UnitOfWorkTests.UnitOfWork(session));
        var text = "seen";

        var updated = await reviews.BulkUpdateAsync(Spec<Review>.Where(r => r.Stars >= 2), s => s.SetProperty(r => r.Text, text).SetProperty(r => r.Stars, r => r.Stars * 2 + 1));

        Assert.Equal(1, updated); // anna's published review in acme: bruno's is not hers, her draft not published, the one at ALPE the tenant other's
        List<Review> stored;
        using (_toggle.UseMode(FilterMode.Raw))
            stored = await session.Query<Review>().ToListAsync();
        Assert.Equal(["anna:anna draft:10", "anna:anna@ALPE:9", "anna:seen:19", "bruno:bruno@RIVA:10"], stored.Select(r => $"{r.OwnerId}:{r.Text}:{r.Stars}").Order(StringComparer.Ordinal).ToList());
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_delete_of_a_soft_deleted_entity_marks_its_documents_and_deletes_them_when_suspended(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await UnitOfWorkTests.AnnaAsync(servers, store, _toggle);
        var properties = await UnitOfWorkTests.PropertiesAsync(session, _toggle);
        var (riva, mare) = (properties.Single(p => p.Name == "RIVA").Id, properties.Single(p => p.Name == "MARE").Id);
        IBulkRepository<Reservation> reservations = new Reservation.DocRepository(UnitOfWorkTests.UnitOfWork(session));

        Assert.Equal(1, await reservations.BulkDeleteAsync(Spec<Reservation>.Where(r => r.PropertyId == riva))); // anna's at RIVA: bruno's is not hers, the other one deleted already
        int erased;
        using (SoftDeleteScope.Suspend())
            erased = await reservations.BulkDeleteAsync(Spec<Reservation>.Where(r => r.PropertyId == mare));

        Assert.Equal(1, erased);
        var stored = await UnitOfWorkTests.ReservationsAsync(session, _toggle);
        Assert.Equal(3, stored.Count); // the one at MARE erased, the one at RIVA marked, not removed
        var marked = Assert.Single(stored, r => r.IsDeleted && r.Nights == 3);
        Assert.Equal(("anna", riva, UnitOfWorkTests.Now, "anna"), (marked.OwnerId, marked.PropertyId, marked.DeletedAt!.Value, marked.DeletedBy));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task An_insert_stamps_the_caller_a_delete_stays_in_the_callers_documents_and_an_upsert_replaces_by_key(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await UnitOfWorkTests.AnnaAsync(servers, store, _toggle);
        IBulkRepository<Review> reviews = new Review.DocRepository(UnitOfWorkTests.UnitOfWork(session));
        var (first, second) = (new Review { Text = "new 1", Published = true }, new Review { Text = "new 2", Published = true });

        Assert.Equal(2, await reviews.BulkInsertAsync([first, second], CancellationToken.None));
        Assert.Equal(1, await reviews.BulkDeleteAsync(Spec<Review>.Where(r => r.Text == "new 1")));
        Assert.Equal(0, await reviews.BulkDeleteAsync(Spec<Review>.Where(r => r.Text == "bruno@RIVA"))); // bruno's: not hers
        second.Text = "new 2b";
        Assert.Equal(2, await reviews.BulkUpsertAsync([second, new Review { Text = "new 3", Published = true }], CancellationToken.None));

        List<Review> stored;
        using (_toggle.UseMode(FilterMode.Raw))
            stored = await session.Query<Review>().ToListAsync();
        Assert.Equal(["acme:anna:anna draft", "acme:anna:anna@RIVA", "acme:anna:new 2b", "acme:anna:new 3", "acme:bruno:bruno@RIVA", "other:anna:anna@ALPE"],
            stored.Select(r => $"{r.TenantId}:{r.OwnerId}:{r.Text}").Order(StringComparer.Ordinal).ToList());
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task An_upsert_never_writes_over_a_document_the_caller_may_not_read(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await UnitOfWorkTests.AnnaAsync(servers, store, _toggle);
        var alpe = (await UnitOfWorkTests.PropertiesAsync(session, _toggle)).Single(p => p.Name == "ALPE"); // the tenant other's
        IBulkRepository<Property> properties = new Property.DocRepository(UnitOfWorkTests.UnitOfWork(session));
        alpe.Name = "ALPE taken";

        if (store == Store.Mongo)
            await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => properties.BulkUpsertAsync([alpe], CancellationToken.None)); // inserted as acme's: the key is taken
        else
            await properties.BulkUpsertAsync([alpe], CancellationToken.None); // inserted as acme's, in acme's partition: the id is free there

        var stored = (await UnitOfWorkTests.PropertiesAsync(session, _toggle)).Where(p => p.Id == alpe.Id).Select(p => $"{p.TenantId}:{p.Name}").Order(StringComparer.Ordinal);
        Assert.Equal(store == Store.Mongo ? ["other:ALPE"] : ["acme:ALPE taken", "other:ALPE"], stored);
    }

    [Fact]
    public async Task An_update_the_generator_did_not_see_says_so()
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await UnitOfWorkTests.AnnaAsync(servers, Store.Mongo, _toggle);
        IBulkRepository<Review> reviews = new Review.DocRepository(UnitOfWorkTests.UnitOfWork(session));
        Func<ISpecification<Review>, Action<IBulkSetters<Review>>, CancellationToken, Task<int>> notACall = reviews.BulkUpdateAsync; // a method group: no call site to compile

        var refused = await Assert.ThrowsAsync<NotSupportedException>(() => notACall(Spec<Review>.Where(r => r.Stars > 0), s => s.SetProperty(r => r.Stars, 1), CancellationToken.None));

        Assert.Contains("compiled where it is called", refused.Message, StringComparison.Ordinal);
    }
}
