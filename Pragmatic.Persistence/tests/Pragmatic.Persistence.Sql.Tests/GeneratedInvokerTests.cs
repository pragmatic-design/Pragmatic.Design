using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Pragmatic.Result.Database;
using Pragmatic.Result.Http;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     Pragmatic's operations over the entities, their invokers generated as an application's are, on the five databases: the reads
///     the invoker makes before the action runs go through the Sql engine's repository, inside the caller's filters.
/// </summary>
[Collection("pragmatic databases")]
public class GeneratedInvokerTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<(SqlSession Session, Dictionary<string, Guid> Ids)> Seeded(SqlDialect dialect)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        using (_toggle.UseMode(FilterMode.Raw))
            return (session, (await session.Query<Review>().ToListAsync()).ToDictionary(r => r.Text, r => r.Id));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task LoadEntity_reads_by_key_inside_the_callers_filters(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, ids) = await Seeded(dialect);
        using (session)
        {
            var invoker = new ReadReviewAction.Invoker(new Review.SqlReadRepository(session), new ServiceCollection().BuildServiceProvider());

            var mine = await invoker.InvokeAsync(new ReadReviewAction { Id = ids["anna@RIVA"] });
            Assert.True(mine.IsSuccess);
            Assert.Equal("anna@RIVA", mine.Value);

            var hidden = await invoker.InvokeAsync(new ReadReviewAction { Id = ids["bruno@RIVA"] }); // bruno's: not the caller's
            Assert.IsType<NotFoundError>(hidden.Error);
        }
    }

    /// <summary>
    ///     <c>[LoadEntity(Include)]</c>: the entity read by key with its navigation, compiled by the engine, the navigation's rows inside the
    ///     caller's filters too (a deleted room type is not among them).
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task LoadEntity_with_an_include_reads_the_navigation(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await Seeded(dialect);
        using (session)
        {
            Guid riva;
            using (_toggle.UseMode(FilterMode.Raw))
                riva = (await session.Query<Property>().ToListAsync()).Single(p => p.Name == "RIVA").Id;
            var invoker = new ReadRoomTypesAction.Invoker(new Property.SqlReadRepository(session), new ServiceCollection().BuildServiceProvider());

            var types = await invoker.InvokeAsync(new ReadRoomTypesAction { Id = riva });

            Assert.Equal(["STD"], types.Value!);
        }
    }

    /// <summary>
    ///     <c>[RequireExists]</c>: the specification the template writes in the invoker (<c>Spec&lt;Review&gt;.Where(__row =&gt; …)</c>), a leaf in
    ///     generated code, answered by the engine inside the caller's filters.
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task RequireExists_answers_inside_the_callers_filters(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, ids) = await Seeded(dialect);
        using (session)
        {
            var invoker = new CheckReviewAction.Invoker(new Review.SqlReadRepository(session), new ServiceCollection().BuildServiceProvider());

            Assert.True((await invoker.InvokeAsync(new CheckReviewAction { Id = ids["anna@MARE"] })).IsSuccess);
            Assert.IsType<NotFoundError>((await invoker.InvokeAsync(new CheckReviewAction { Id = ids["anna@ALPE"] })).Error); // unpublished: the rule hides it
            Assert.IsType<NotFoundError>((await invoker.InvokeAsync(new CheckReviewAction { Id = Guid.NewGuid() })).Error);
        }
    }

    /// <summary><c>[LoadEntities]</c>: one query for the distinct keys (a <c>Contains</c> leaf the template writes), the rows in the keys' order, one 404 naming every key no row answered.</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task LoadEntities_answers_in_the_keys_order_or_names_every_key_missing(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, ids) = await Seeded(dialect);
        using (session)
        {
            var invoker = new ReadReviewsAction.Invoker(new Review.SqlReadRepository(session), new ServiceCollection().BuildServiceProvider());

            var both = await invoker.InvokeAsync(new ReadReviewsAction { Ids = [ids["anna@MARE"], ids["anna@RIVA"], ids["anna@MARE"]] });
            Assert.Equal(["anna@MARE", "anna@RIVA"], both.Value!);

            var unknown = Guid.NewGuid();
            var missing = await invoker.InvokeAsync(new ReadReviewsAction { Ids = [ids["anna@RIVA"], ids["bruno@RIVA"], unknown] });
            var notFound = Assert.IsType<NotFoundError>(missing.Error);
            Assert.Equal($"{ids["bruno@RIVA"]}, {unknown}", notFound.EntityId); // bruno's is hidden from anna as an unknown key is

            Assert.Empty((await invoker.InvokeAsync(new ReadReviewsAction { Ids = null })).Value!);
        }
    }

    /// <summary>
    ///     A declared query's invoker on the Sql engine: the read goes through the entity's repository, which runs the query compiled
    ///     (<c>RunAsync</c>), inside the caller's filters; the page comes back whole.
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_declared_querys_invoker_reads_through_the_repository(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await Seeded(dialect);
        using (session)
        {
            var services = new ServiceCollection()
                .AddScoped<IReadRepository<Review>>(_ => new Review.SqlReadRepository(session))
                .BuildServiceProvider();

            var page = await new SearchReviewsQuery.Invoker(services).RunAsync(new SearchReviewsQuery());

            Assert.Equal(["anna@RIVA", "anna@MARE"], page.Value!.Items.Select(r => r.Text));
            Assert.Equal(2, page.Value.TotalCount);

            var one = await new ReviewByTextQuery.Invoker(services).RunAsync(new ReviewByTextQuery { Text = "anna@MARE" });
            Assert.Equal("anna@MARE", one.Value!.Text);
            var none = await new ReviewByTextQuery.Invoker(services).RunAsync(new ReviewByTextQuery { Text = "bruno@RIVA" }); // not hers
            Assert.IsType<NotFoundError>(none.Error);
        }
    }

    /// <summary>
    ///     The mutation invokers' writes on the Sql engine's repository and unit of work: a create adds and saves; an update loads by key (a
    ///     404 when nothing answers), applies and saves; a save that breaks a rule goes on as the violation, the entity detached, the unit of
    ///     work still usable.
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Mutations_create_and_update_through_the_contracts_and_a_failed_save_detaches(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
        var amenities = new Amenity.SqlRepository(unitOfWork);
        var services = new ServiceCollection().BuildServiceProvider();
        var create = new CreateAmenityMutation.Invoker(amenities, unitOfWork, services);
        var rename = new RenameAmenityMutation.Invoker(amenities, unitOfWork, services);

        var wifi = (await create.InvokeAsync(new CreateAmenityMutation { Name = "WiFi" })).Value!;
        var spa = (await create.InvokeAsync(new CreateAmenityMutation { Name = "Spa" })).Value!;
        var renamed = await rename.InvokeAsync(new RenameAmenityMutation { Id = wifi.Id, Name = "WiFi 6" });
        Assert.Equal("WiFi 6", renamed.Value!.Name);

        var missing = await rename.InvokeAsync(new RenameAmenityMutation { Id = Guid.CreateVersion7(), Name = "none" });
        Assert.IsType<NotFoundError>(missing.Error);

        // A save that breaks a rule: the failure goes on, the entity is forgotten, the unit of work stays usable.
        var duplicate = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => rename.InvokeAsync(new RenameAmenityMutation { Id = spa.Id, Name = "WiFi 6" }));
        Assert.IsType<DbConflictError>(duplicate.Error);
        Assert.Null(unitOfWork.EntryOf(spa));
        Assert.Equal("Spa", (await UnitOfWorkTests.RawAsync<Amenity>(session, _toggle)).Single(a => a.Id == spa.Id).Name);
        Assert.Equal("Spa", (await rename.InvokeAsync(new RenameAmenityMutation { Id = spa.Id, Name = "Spa" })).Value!.Name); // read again, as stored
    }
}
