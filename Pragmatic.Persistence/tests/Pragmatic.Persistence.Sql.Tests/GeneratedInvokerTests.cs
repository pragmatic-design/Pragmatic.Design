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
    ///     A mutation's load with <c>[EagerLoad]</c>: the entity read by key with its navigation, compiled by the engine for the paths the
    ///     generator knows at build, then changed and saved.
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_mutations_load_reads_its_eager_navigation(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await Seeded(dialect);
        using (session)
        {
            Guid riva;
            using (_toggle.UseMode(FilterMode.Raw))
                riva = (await session.Query<Property>().ToListAsync()).Single(p => p.Name == "RIVA").Id;
            var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
            var invoker = new RenamePropertyMutation.Invoker(new Property.SqlRepository(unitOfWork), unitOfWork, new ServiceCollection().BuildServiceProvider());

            var renamed = await invoker.InvokeAsync(new RenamePropertyMutation { Id = riva, Name = "RIVA DEL SOLE" });

            Assert.Equal("RIVA DEL SOLE", renamed.Value!.Name);
            Assert.Equal(["STD"], renamed.Value.RoomTypes.Select(t => t.Code));
        }
    }

    /// <summary>
    ///     A mutation that answers with a DTO reading a navigation, and declares no <c>[EagerLoad]</c>: the load reads what the response maps
    ///     from (its <c>RequiredNavigations</c>, computed at build), so the DTO built from the entity carries the room types.
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_mutation_answering_with_a_dto_loads_what_the_dto_maps_from(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await Seeded(dialect);
        using (session)
        {
            Guid riva;
            using (_toggle.UseMode(FilterMode.Raw))
                riva = (await session.Query<Property>().ToListAsync()).Single(p => p.Name == "RIVA").Id;
            var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
            var invoker = new RetitlePropertyMutation.Invoker(new Property.SqlRepository(unitOfWork), unitOfWork, new ServiceCollection().BuildServiceProvider());

            var retitled = await invoker.InvokeAsync(new RetitlePropertyMutation { Id = riva, Name = "RIVA NUOVA" });

            var response = PropertyRoomsDto.FromEntity(retitled.Value!);
            Assert.Equal("RIVA NUOVA", response.Name);
            Assert.Equal(["STD"], response.RoomTypes.Select(t => t.Code));
        }
    }

    /// <summary>
    ///     A keyed merge of children on the Sql engine: the load reads the collection, the unit of work saves the graph. A child still sent is
    ///     updated, a new one inserted under its property (the foreign key set as EF Core's fixup sets it), one not sent any more deleted, a
    ///     soft one marked; the rows read back say so.
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_merge_of_children_inserts_updates_and_removes_them(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await Seeded(dialect);
        using (session)
        {
            Guid riva, standard;
            using (_toggle.UseMode(FilterMode.Raw))
            {
                riva = (await session.Query<Property>().ToListAsync()).Single(p => p.Name == "RIVA").Id;
                standard = (await session.Query<RoomType>().ToListAsync()).Single(t => t.Code == "STD").Id;
            }

            var services = new ServiceCollection().BuildServiceProvider();
            async Task Set(params WriteRoomTypeMutation[] types)
            {
                var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
                var set = await new SetRoomTypesMutation.Invoker(new Property.SqlRepository(unitOfWork), unitOfWork, services)
                    .InvokeAsync(new SetRoomTypesMutation { Id = riva, RoomTypes = [.. types] });
                Assert.True(set.IsSuccess, set.IsSuccess ? "" : set.Error.ToString());
            }

            async Task<List<RoomType>> Rows()
            {
                using (_toggle.UseMode(FilterMode.Raw))
                    return await session.Query<RoomType>().ToListAsync();
            }

            await Set(new WriteRoomTypeMutation { Id = standard, Code = "STD+" }, new WriteRoomTypeMutation { Code = "FAM" });
            var rows = await Rows();
            Assert.Equal(["FAM", "STD+"], rows.Where(t => !t.IsDeleted).Select(t => t.Code).Order());
            var family = rows.Single(t => t.Code == "FAM");
            Assert.Equal(riva, family.PropertyId);
            Assert.Equal(standard, rows.Single(t => t.Code == "STD+").Id); // updated in place, not replaced

            await Set(new WriteRoomTypeMutation { Id = family.Id, Code = "FAM" });
            rows = await Rows();
            Assert.Equal(["FAM"], rows.Where(t => !t.IsDeleted).Select(t => t.Code));
            Assert.True(rows.Single(t => t.Code == "STD+").IsDeleted); // a soft child: marked, its row kept
        }
    }

    /// <summary>A create with children on the Sql engine: the new root and every child of its collections inserted, each child under the root.</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_create_with_children_inserts_the_graph(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await Seeded(dialect);
        using (session)
        {
            var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
            var invoker = new OpenPropertyMutation.Invoker(new Property.SqlRepository(unitOfWork), unitOfWork, new ServiceCollection().BuildServiceProvider());

            var opened = await invoker.InvokeAsync(new OpenPropertyMutation { Name = "LAGO", RoomTypes = [new() { Code = "DBL" }, new() { Code = "TPL" }] });

            Assert.True(opened.IsSuccess, opened.IsSuccess ? "" : opened.Error.ToString());
            using (_toggle.UseMode(FilterMode.Raw))
            {
                var lago = (await session.Query<Property>().ToListAsync()).Single(p => p.Name == "LAGO");
                Assert.Equal(["DBL", "TPL"], (await session.Query<RoomType>().ToListAsync()).Where(t => t.PropertyId == lago.Id).Select(t => t.Code).Order());
            }
        }
    }

    /// <summary>
    ///     A delete whose soft delete cascades, then its restore, on the Sql engine: the load reads the beds, the delete marks them with the room
    ///     type's instant, the restore brings back those and only those (a bed deleted on its own stays deleted).
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_cascading_delete_marks_the_children_and_its_restore_brings_back_only_those(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await Seeded(dialect);
        using (session)
        {
            Guid standard;
            using (_toggle.UseMode(FilterMode.Raw))
            {
                standard = (await session.Query<RoomType>().ToListAsync()).Single(t => t.Code == "STD").Id;
                foreach (var label in new[] { "A", "B", "OLD" })
                {
                    var bed = new Bed { Label = label };
                    bed.SetRoomTypeId(standard);
                    await session.InsertAsync(bed);
                    if (label == "OLD")
                        await session.DeleteAsync(bed); // deleted on its own, before the room type
                }
            }

            async Task<List<Bed>> Beds()
            {
                using (_toggle.UseMode(FilterMode.Raw))
                    return await session.Query<Bed>().ToListAsync();
            }

            var services = new ServiceCollection().BuildServiceProvider();
            var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
            var closed = await new CloseRoomTypeMutation.Invoker(new RoomType.SqlRepository(unitOfWork), unitOfWork, services).InvokeAsync(new CloseRoomTypeMutation { Id = standard });
            Assert.True(closed.IsSuccess, closed.IsSuccess ? "" : closed.Error.ToString());
            var beds = await Beds();
            Assert.All(beds, b => Assert.True(b.IsDeleted));
            DateTimeOffset? hiddenAt; // as stored: a database keeps fewer digits of an instant than memory does
            using (_toggle.UseMode(FilterMode.Raw))
                hiddenAt = (await session.Query<RoomType>().ToListAsync()).Single(t => t.Id == standard).DeletedAt;
            Assert.Equal(["A", "B"], beds.Where(b => b.DeletedAt == hiddenAt).Select(b => b.Label).Order());

            unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
            var restored = await new RestoreRoomTypeMutation.Invoker(new RoomType.SqlRepository(unitOfWork), unitOfWork, services, filterToggle: _toggle)
                .InvokeAsync(new RestoreRoomTypeMutation { Id = standard });
            Assert.True(restored.IsSuccess, restored.IsSuccess ? "" : restored.Error.ToString());
            beds = await Beds();
            Assert.Equal(["A", "B"], beds.Where(b => !b.IsDeleted).Select(b => b.Label).Order());
            Assert.True(beds.Single(b => b.Label == "OLD").IsDeleted);
        }
    }

    /// <summary>
    ///     A restore on the Sql engine: the invoker reads the soft-deleted row by key with the entity's soft delete filter lifted, and saves it
    ///     restored; every other filter stays, so a caller cannot restore a row that is not theirs.
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_restore_lifts_the_soft_delete_filter_and_no_other(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await Seeded(dialect);
        using (session)
        {
            Guid suite, annas, brunos;
            using (_toggle.UseMode(FilterMode.Raw))
            {
                suite = (await session.Query<RoomType>().ToListAsync()).Single(t => t.Code == "SUI").Id;
                var stays = await session.Query<Reservation>().ToListAsync();
                var (hers, his) = (stays.First(r => r.OwnerId == "anna"), stays.Single(r => r.OwnerId == "bruno"));
                await session.DeleteAsync(hers);
                await session.DeleteAsync(his);
                (annas, brunos) = (hers.Id, his.Id);
            }

            var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
            var services = new ServiceCollection().BuildServiceProvider();
            var restoreType = new RestoreRoomTypeMutation.Invoker(new RoomType.SqlRepository(unitOfWork), unitOfWork, services, filterToggle: _toggle);
            var restoreStay = new RestoreReservationMutation.Invoker(new Reservation.SqlRepository(unitOfWork), unitOfWork, services, filterToggle: _toggle);

            var restoredType = await restoreType.InvokeAsync(new RestoreRoomTypeMutation { Id = suite });
            Assert.True(restoredType.IsSuccess, restoredType.IsSuccess ? "" : restoredType.Error.ToString());
            Assert.Equal(["STD", "SUI"], (await session.Query<RoomType>().ToListAsync()).Select(t => t.Code).Order());

            Assert.True((await restoreStay.InvokeAsync(new RestoreReservationMutation { Id = annas })).IsSuccess);
            Assert.IsType<NotFoundError>((await restoreStay.InvokeAsync(new RestoreReservationMutation { Id = brunos })).Error); // bruno's: not anna's
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
