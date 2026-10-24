using Pragmatic.Authorization;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Pragmatic;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     Pragmatic's <c>IUnitOfWork</c> and <c>IRepository&lt;T&gt;</c> on the documents engine (<see cref="DocUnitOfWork" />, the generated
///     <c>{Entity}.DocRepository</c>), written through as Pragmatic's generated code writes: load, change, save. The expected values are the Sql
///     engine's (<c>UnitOfWorkTests</c> there), which are Pragmatic's EF Core interceptors'. MongoDB (a replica set) and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class UnitOfWorkTests(DocumentServers servers)
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    /// <summary>anna's session on the seeded documents (<see cref="DocumentRows" />), the amenities' container there too.</summary>
    internal static async Task<DocSession> AnnaAsync(DocumentServers servers, Store store, IQueryFilterToggle toggle)
    {
        var seeded = await DocumentRows.SeededAsync(servers, store);
        if (store == Store.Cosmos)
            await seeded.EnsureContainerAsync<Amenity>();
        return DocumentServers.As(seeded, DocumentRows.Anna(toggle));
    }

    internal static DocUnitOfWork UnitOfWork(DocSession session, RecordingDispatcher? dispatcher = null, params IDocSavePhase[] phases)
        => new(session, new FixedClock(Now), new TestUser("anna"), new TenantScope(), dispatcher: dispatcher, phases: phases);

    /// <summary>Every document of the type, whoever's (Raw).</summary>
    internal static async Task<List<Property>> PropertiesAsync(DocSession session, IQueryFilterToggle toggle)
    {
        using (toggle.UseMode(FilterMode.Raw))
            return await session.Query<Property>().ToListAsync();
    }

    internal static async Task<List<Reservation>> ReservationsAsync(DocSession session, IQueryFilterToggle toggle)
    {
        using (toggle.UseMode(FilterMode.Raw))
            return await session.Query<Reservation>().ToListAsync();
    }

    internal static async Task<List<Amenity>> AmenitiesAsync(DocSession session, IQueryFilterToggle toggle)
    {
        using (toggle.UseMode(FilterMode.Raw))
            return await session.Query<Amenity>().ToListAsync();
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_loaded_entity_saves_its_changes_with_the_audit_stamps(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await AnnaAsync(servers, store, _toggle);
        var riva = (await PropertiesAsync(session, _toggle)).Single(p => p.Name == "RIVA");
        var unitOfWork = UnitOfWork(session);
        IRepository<Property> properties = new Property.DocRepository(unitOfWork);

        var loaded = (await properties.GetByIdAsync(riva.Id))!;
        Assert.Same(loaded, await properties.GetByIdAsync(riva.Id)); // the identity map

        // Another writer changes a field this unit of work does not touch.
        riva.GrantScope("desk-east");
        await session.ReplaceAsync(riva);

        loaded.Name = "RIVA nuova";
        if (store == Store.Cosmos)
        {
            // Cosmos DB replaces the document whole, at the etag it was read with: refused rather than the other writer's scope lost (owner's decision).
            var stale = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => unitOfWork.SaveChangesAsync());
            Assert.Equal("Property", Assert.IsType<ConcurrencyError>(stale.Error).EntityTypeName);
            return;
        }

        // MongoDB sets the changed fields only: the other writer's scope survives.
        Assert.Equal(1, await unitOfWork.SaveChangesAsync());
        Assert.Equal(0, await unitOfWork.SaveChangesAsync()); // nothing changed since
        var stored = (await PropertiesAsync(session, _toggle)).Single(p => p.Id == riva.Id);
        Assert.Equal(("RIVA nuova", Now, "anna", riva.CreatedBy, riva.CreatedAt), (stored.Name, stored.UpdatedAt!.Value, stored.UpdatedBy, stored.CreatedBy, stored.CreatedAt));
        Assert.Equal(["desk-north", "desk-east"], stored.AccessScopes);
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_loaded_entity_saved_twice_keeps_the_etag_and_version_it_wrote(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await AnnaAsync(servers, store, _toggle);
        var unitOfWork = UnitOfWork(session);
        var riva = (await new Property.DocRepository(unitOfWork).FindAsync(Spec<Property>.True)).Single();
        foreach (var name in new[] { "RIVA 1", "RIVA 2" })
        {
            riva.Name = name;
            Assert.Equal(1, await unitOfWork.SaveChangesAsync()); // the second write is at what the first one left (Cosmos DB: its etag)
        }

        Assert.Equal("RIVA 2", (await PropertiesAsync(session, _toggle)).Single(p => p.Id == riva.Id).Name);
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task An_added_entity_takes_the_callers_tenant_owner_scope_and_stamps(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await AnnaAsync(servers, store, _toggle);
        var unitOfWork = UnitOfWork(session);
        var neve = new Property { Name = "NEVE", TenantId = "other" }; // a tenant the caller is not: overwritten, as TenantInterceptor does
        var stay = new Reservation { CheckIn = Now };
        stay.SetPropertyId(neve.Id);
        new Reservation.DocRepository(unitOfWork).Add(stay);
        new Property.DocRepository(unitOfWork).Add(neve);

        Assert.Equal(2, await unitOfWork.SaveChangesAsync());

        var property = (await PropertiesAsync(session, _toggle)).Single(p => p.Name == "NEVE");
        Assert.Equal(("acme", Now, "anna", Now, "anna"), (property.TenantId, property.CreatedAt, property.CreatedBy, property.UpdatedAt!.Value, property.UpdatedBy));
        Assert.Equal([ScopeIdentifiers.ForUser("anna")], property.AccessScopes);
        Assert.Equal("anna", (await ReservationsAsync(session, _toggle)).Single(r => r.Id == stay.Id).OwnerId);
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_removed_entity_is_marked_when_soft_deleted_and_deleted_otherwise(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await AnnaAsync(servers, store, _toggle);
        var dispatcher = new RecordingDispatcher();
        var unitOfWork = UnitOfWork(session, dispatcher);
        IRepository<Reservation> reservations = new Reservation.DocRepository(unitOfWork);
        IRepository<Amenity> amenities = new Amenity.DocRepository(unitOfWork);
        var wifi = new Amenity { Name = "WiFi" };
        amenities.Add(wifi);
        await unitOfWork.SaveChangesAsync();

        var mine = await reservations.FindAsync(Spec<Reservation>.True);
        reservations.Remove(mine[0]);
        amenities.Remove((await amenities.GetByIdAsync(wifi.Id))!);
        Assert.Equal(2, await unitOfWork.SaveChangesAsync());

        var marked = (await ReservationsAsync(session, _toggle)).Single(r => r.Id == mine[0].Id);
        Assert.Equal((true, Now, "anna"), (marked.IsDeleted, marked.DeletedAt!.Value, marked.DeletedBy));
        Assert.Equal(mine.Count - 1, (await reservations.FindAsync(Spec<Reservation>.True)).Count);
        Assert.DoesNotContain(await AmenitiesAsync(session, _toggle), a => a.Id == wifi.Id);
        Assert.Equal([Pragmatic.Events.EntityLifecycle.Created, Pragmatic.Events.EntityLifecycle.Deleted], dispatcher.Dispatched.OfType<AmenityChanged>().Select(e => e.Lifecycle));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_tenant_transfer_is_reverted_and_a_private_setter_change_is_saved(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await AnnaAsync(servers, store, _toggle);
        var mare = (await PropertiesAsync(session, _toggle)).Single(p => p.Name == "MARE");
        var unitOfWork = UnitOfWork(session);
        var riva = (await new Property.DocRepository(unitOfWork).FindAsync(Spec<Property>.True)).Single();
        riva.TenantId = "other";
        riva.Name = "RIVA moved";
        var stay = (await new Reservation.DocRepository(unitOfWork).FindAsync(Spec<Reservation>.True)).First(r => r.PropertyId == riva.Id);
        stay.SetPropertyId(mare.Id); // the shape's internal setter: the snapshot sees it like a public one

        Assert.Equal(2, await unitOfWork.SaveChangesAsync());

        var stored = (await PropertiesAsync(session, _toggle)).Single(p => p.Id == riva.Id);
        Assert.Equal(("acme", "RIVA moved"), (stored.TenantId, stored.Name));
        Assert.Equal(mare.Id, (await ReservationsAsync(session, _toggle)).Single(r => r.Id == stay.Id).PropertyId);
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_document_the_caller_may_not_read_is_not_written_by_key(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await AnnaAsync(servers, store, _toggle);
        var alpe = (await PropertiesAsync(session, _toggle)).Single(p => p.Name == "ALPE"); // the tenant other's
        var unitOfWork = UnitOfWork(session);
        alpe.Name = "ALPE taken";
        new Property.DocRepository(unitOfWork).Update(alpe);

        var refused = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => unitOfWork.SaveChangesAsync());
        Assert.Equal(404, refused.Error.StatusCode);
        Assert.Equal("ALPE", (await PropertiesAsync(session, _toggle)).Single(p => p.Id == alpe.Id).Name);
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_modules_phases_and_the_generated_values_write_with_the_entities(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await AnnaAsync(servers, store, _toggle);
        var unitOfWork = new DocUnitOfWork(session, new FixedClock(Now), new TestUser("anna"), new TenantScope(), generatedValues: [new ReviewTextBinding()], phases: [new JournalPhase()]);
        var review = new Review { Published = true, Stars = 4 };
        new Review.DocRepository(unitOfWork).Add(review);

        Assert.Equal(1, await unitOfWork.SaveChangesAsync());

        using (_toggle.UseMode(FilterMode.Raw))
            Assert.Equal("generated by anna at 2026-10-22", (await session.Query<Review>().ToListAsync()).Single(r => r.Id == review.Id).Text);
        Assert.Equal(["journal:Review:Added"], (await AmenitiesAsync(session, _toggle)).Select(a => a.Name));
    }

    [Fact]
    public async Task A_tenants_entity_is_refused_without_a_tenant_when_one_is_required()
    {
        var unitOfWork = new DocUnitOfWork(servers.Session(Store.Mongo), new FixedClock(Now), new TestUser("anna"), new TenantScope(), requireTenant: true); // no tenant in scope
        new Property.DocRepository(unitOfWork).Add(new Property { Name = "NEVE", TenantId = "acme" });

        await Assert.ThrowsAsync<TenantNotResolvedException>(() => unitOfWork.SaveChangesAsync());
    }
}
