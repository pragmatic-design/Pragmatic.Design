using Pragmatic.Authorization;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     Pragmatic's <c>IUnitOfWork</c> and <c>IRepository&lt;T&gt;</c> on the Sql engine (<see cref="SqlUnitOfWork" />, the generated
///     <c>{Entity}.SqlRepository</c>), written through as Pragmatic's generated code writes: load, change, save. The expected values are the ones
///     Pragmatic's EF Core interceptors write (Auditing, SoftDelete, Ownership, Scope, Tenant), read from their source. Five databases.
/// </summary>
[Collection("pragmatic databases")]
public class UnitOfWorkTests(PragmaticDatabases databases)
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    /// <summary>anna, of acme, holding desk-north; the rows of <see cref="PragmaticRows" />.</summary>
    internal static async Task<SqlSession> SeededAsync(PragmaticDatabases databases, SqlDialect dialect, IQueryFilterToggle toggle)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), toggle));
        await PragmaticRows.SeedAsync(session, toggle);
        return session;
    }

    /// <summary>Every row of the type, whoever's (Raw): one chain per type, a chain on an open type is not translated.</summary>
    internal static async Task<List<T>> RawAsync<T>(SqlSession session, IQueryFilterToggle toggle)
    {
        using (toggle.UseMode(FilterMode.Raw))
        {
            object rows = typeof(T) == typeof(Property) ? await session.Query<Property>().ToListAsync()
                : typeof(T) == typeof(Reservation) ? await session.Query<Reservation>().ToListAsync()
                : typeof(T) == typeof(Review) ? await session.Query<Review>().ToListAsync()
                : typeof(T) == typeof(Amenity) ? await session.Query<Amenity>().ToListAsync()
                : throw new NotSupportedException(typeof(T).Name);
            return (List<T>)rows;
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_loaded_entity_saves_its_changed_columns_with_the_audit_stamps(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await SeededAsync(databases, dialect, _toggle);
        var riva = (await RawAsync<Property>(session, _toggle)).Single(p => p.Name == "RIVA");
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(Now), new TestUser("anna"), new TenantScope());
        IRepository<Property> properties = new Property.SqlRepository(unitOfWork);

        var loaded = (await properties.GetByIdAsync(riva.Id))!;
        Assert.Same(loaded, await properties.GetByIdAsync(riva.Id)); // the identity map

        // Another writer changes a column this unit of work does not touch: the save writes only the columns that changed, so it survives.
        using (_toggle.UseMode(FilterMode.Raw))
        {
            riva.GrantScope("desk-east");
            await session.UpdateAsync(riva);
        }

        loaded.Name = "RIVA nuova";
        Assert.Equal(1, await unitOfWork.SaveChangesAsync());
        Assert.Equal(0, await unitOfWork.SaveChangesAsync()); // nothing changed since

        var stored = (await RawAsync<Property>(session, _toggle)).Single(p => p.Id == riva.Id);
        Assert.Equal(("RIVA nuova", Now, "anna", riva.CreatedBy, riva.CreatedAt), (stored.Name, stored.UpdatedAt!.Value, stored.UpdatedBy, stored.CreatedBy, stored.CreatedAt));
        Assert.Equal(["desk-north", "desk-east"], stored.AccessScopes);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task An_added_entity_takes_the_callers_tenant_owner_scope_and_stamps(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await SeededAsync(databases, dialect, _toggle);
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(Now), new TestUser("anna"), new TenantScope());
        var neve = new Property { Name = "NEVE", TenantId = "other" }; // a tenant the caller is not: overwritten, as TenantInterceptor does
        var stay = new Reservation { CheckIn = Now };
        stay.SetPropertyId(neve.Id);
        new Reservation.SqlRepository(unitOfWork).Add(stay); // the referrer first: the save orders the inserts
        new Property.SqlRepository(unitOfWork).Add(neve);

        Assert.Equal(2, await unitOfWork.SaveChangesAsync());

        var property = (await RawAsync<Property>(session, _toggle)).Single(p => p.Name == "NEVE");
        Assert.Equal(("acme", Now, "anna", Now, "anna"), (property.TenantId, property.CreatedAt, property.CreatedBy, property.UpdatedAt!.Value, property.UpdatedBy));
        Assert.Equal([ScopeIdentifiers.ForUser("anna")], property.AccessScopes);
        Assert.Equal("anna", (await RawAsync<Reservation>(session, _toggle)).Single(r => r.Id == stay.Id).OwnerId);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_removed_entity_is_marked_when_soft_deleted_and_deleted_otherwise(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await SeededAsync(databases, dialect, _toggle);
        var dispatcher = new RecordingDispatcher();
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(Now), new TestUser("anna"), new TenantScope(), dispatcher: dispatcher);
        IRepository<Reservation> reservations = new Reservation.SqlRepository(unitOfWork);
        IRepository<Amenity> amenities = new Amenity.SqlRepository(unitOfWork);
        var wifi = new Amenity { Name = "WiFi" };
        amenities.Add(wifi);
        await unitOfWork.SaveChangesAsync();

        var mine = await reservations.FindAsync(Pragmatic.Specification.Spec<Reservation>.True);
        reservations.Remove(mine[0]);
        amenities.Remove((await amenities.GetByIdAsync(wifi.Id))!);
        Assert.Equal(2, await unitOfWork.SaveChangesAsync());

        var marked = (await RawAsync<Reservation>(session, _toggle)).Single(r => r.Id == mine[0].Id);
        Assert.Equal((true, Now, "anna"), (marked.IsDeleted, marked.DeletedAt!.Value, marked.DeletedBy));
        Assert.Equal(mine.Count - 1, (await reservations.FindAsync(Pragmatic.Specification.Spec<Reservation>.True)).Count);
        Assert.DoesNotContain(await RawAsync<Amenity>(session, _toggle), a => a.Id == wifi.Id);
        // The lifecycle as events, after each commit: the hard-deleted entity's too.
        Assert.Equal([Pragmatic.Events.EntityLifecycle.Created, Pragmatic.Events.EntityLifecycle.Deleted], dispatcher.Dispatched.OfType<AmenityChanged>().Select(e => e.Lifecycle));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_tenant_transfer_is_reverted_and_a_private_setter_change_is_saved(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await SeededAsync(databases, dialect, _toggle);
        var mare = (await RawAsync<Property>(session, _toggle)).Single(p => p.Name == "MARE");
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(Now), new TestUser("anna"), new TenantScope());
        var riva = (await new Property.SqlRepository(unitOfWork).FindAsync(Pragmatic.Specification.Spec<Property>.True)).Single();
        riva.TenantId = "other";
        riva.Name = "RIVA moved";
        var stay = (await new Reservation.SqlRepository(unitOfWork).FindAsync(Pragmatic.Specification.Spec<Reservation>.True)).First(r => r.PropertyId == riva.Id);
        stay.SetPropertyId(mare.Id); // the shape's internal setter: the snapshot sees it like a public one

        Assert.Equal(2, await unitOfWork.SaveChangesAsync());

        var stored = (await RawAsync<Property>(session, _toggle)).Single(p => p.Id == riva.Id);
        Assert.Equal(("acme", "RIVA moved"), (stored.TenantId, stored.Name));
        Assert.Equal(mare.Id, (await RawAsync<Reservation>(session, _toggle)).Single(r => r.Id == stay.Id).PropertyId);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_modules_phases_and_the_generated_values_write_with_the_entities(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await SeededAsync(databases, dialect, _toggle);
        var riva = (await RawAsync<Property>(session, _toggle)).Single(p => p.Name == "RIVA");
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(Now), new TestUser("anna"), new TenantScope(), generatedValues: [new ReviewTextBinding()], phases: [new JournalPhase()]);
        var review = new Review { Published = true, Stars = 4 };
        review.SetPropertyId(riva.Id);
        new Review.SqlRepository(unitOfWork).Add(review);

        Assert.Equal(1, await unitOfWork.SaveChangesAsync());

        Assert.Equal("generated by anna at 2026-10-22", (await RawAsync<Review>(session, _toggle)).Single(r => r.Id == review.Id).Text);
        Assert.Equal(["journal:Review:Added"], (await RawAsync<Amenity>(session, _toggle)).Select(a => a.Name));
    }

    [Fact]
    public async Task A_tenants_entity_is_refused_without_a_tenant_when_one_is_required()
    {
        using var session = await databases.SessionAsync(SqlDialect.Sqlite, null);
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(Now), new TestUser("anna"), new TenantScope(), requireTenant: true); // no tenant in scope
        new Property.SqlRepository(unitOfWork).Add(new Property { Name = "NEVE", TenantId = "acme" });

        await Assert.ThrowsAsync<TenantNotResolvedException>(() => unitOfWork.SaveChangesAsync());
    }
}
