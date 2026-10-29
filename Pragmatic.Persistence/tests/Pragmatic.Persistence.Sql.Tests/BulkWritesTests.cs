using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Pragmatic.Specification;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     <c>IBulkRepository</c> on the Sql engine, on the five databases. The update and the delete are compiled where they are called: one
///     statement over the rows the specification selects among the caller's (anna, in acme, holds desk-north), as EF Core's
///     <c>ExecuteUpdate</c> keeps the query filters. A <c>[SoftDelete]</c> entity's delete marks its rows as the unit of work marks one, and
///     deletes them inside <c>SoftDeleteScope.Suspend</c>. The insert and the upsert are the save's phases applied to the rows alone: the tenant
///     stamped; the upsert updates only a row the caller may read.
/// </summary>
[Collection("pragmatic databases")]
public class BulkWritesTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<(SqlSession Session, SqlUnitOfWork UnitOfWork)> OpenAsync(SqlDialect dialect)
    {
        var anna = new TestUser("anna");
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(anna, new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        return (session, new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), anna, new TenantScope()));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task An_update_sets_a_captured_value_and_an_expression_on_the_callers_rows(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, unitOfWork) = await OpenAsync(dialect);
        using var _ = session;
        IBulkRepository<Review> reviews = new Review.SqlRepository(unitOfWork);
        var text = "seen";

        var updated = await reviews.BulkUpdateAsync(Spec<Review>.Where(r => r.Stars >= 2), s => s.SetProperty(r => r.Text, text).SetProperty(r => r.Stars, r => r.Stars + 1));

        Assert.Equal(2, updated); // anna's published reviews: bruno's is not hers, the one at ALPE not published
        List<Review> rows;
        using (_toggle.UseMode(FilterMode.Raw))
            rows = await session.Query<Review>().ToListAsync();
        Assert.Equal(["anna:anna@ALPE:2", "anna:seen:5", "anna:seen:6", "bruno:bruno@RIVA:3"], rows.Select(r => $"{r.OwnerId}:{r.Text}:{r.Stars}").OrderBy(r => r, StringComparer.Ordinal).ToList());
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_delete_of_a_soft_deleted_entity_marks_its_rows_with_the_instant_and_the_caller(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, unitOfWork) = await OpenAsync(dialect);
        using var _ = session;
        Guid riva;
        using (_toggle.UseMode(FilterMode.Raw))
            riva = (await session.Query<Property>().ToListAsync()).Single(p => p.Name == "RIVA").Id;
        IBulkRepository<Reservation> reservations = new Reservation.SqlRepository(unitOfWork);

        var deleted = await reservations.BulkDeleteAsync(Spec<Reservation>.Where(r => r.PropertyId == riva));

        Assert.Equal(1, deleted); // anna's at RIVA: bruno's is not hers
        List<Reservation> rows;
        using (_toggle.UseMode(FilterMode.Raw))
            rows = await session.Query<Reservation>().ToListAsync();
        Assert.Equal(4, rows.Count); // marked, not removed
        var marked = Assert.Single(rows, r => r.IsDeleted);
        Assert.Equal(("anna", riva, UnitOfWorkTests.Now, "anna"), (marked.OwnerId, marked.PropertyId, marked.DeletedAt!.Value, marked.DeletedBy));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_delete_of_a_soft_deleted_entity_inside_a_suspended_scope_removes_its_rows(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, unitOfWork) = await OpenAsync(dialect);
        using var _ = session;
        Guid mare;
        using (_toggle.UseMode(FilterMode.Raw))
            mare = (await session.Query<Property>().ToListAsync()).Single(p => p.Name == "MARE").Id;
        IBulkRepository<Reservation> reservations = new Reservation.SqlRepository(unitOfWork);

        int erased;
        using (SoftDeleteScope.Suspend())
            erased = await reservations.BulkDeleteAsync(Spec<Reservation>.Where(r => r.PropertyId == mare));

        Assert.Equal(1, erased); // anna's at MARE
        List<Reservation> rows;
        using (_toggle.UseMode(FilterMode.Raw))
            rows = await session.Query<Reservation>().ToListAsync();
        Assert.Equal(3, rows.Count); // erased, as the unit of work erases one inside the scope
        Assert.DoesNotContain(rows, r => r.IsDeleted || r.PropertyId == mare);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task An_upsert_never_writes_over_a_row_the_caller_may_not_read(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, unitOfWork) = await OpenAsync(dialect);
        using var _ = session;
        Property alpe;
        using (_toggle.UseMode(FilterMode.Raw))
            alpe = (await session.Query<Property>().ToListAsync()).Single(p => p.Name == "ALPE"); // the tenant other's
        IBulkRepository<Property> properties = new Property.SqlRepository(unitOfWork);
        alpe.Name = "ALPE taken";

        await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => properties.BulkUpsertAsync([alpe], CancellationToken.None)); // inserted as acme's: the key is taken

        List<Property> rows;
        using (_toggle.UseMode(FilterMode.Raw))
            rows = await session.Query<Property>().ToListAsync();
        Assert.Equal(["other:ALPE"], rows.Where(p => p.Id == alpe.Id).Select(p => $"{p.TenantId}:{p.Name}").ToList());
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task An_insert_stamps_the_tenant_a_delete_removes_the_tenants_rows_and_an_upsert_updates_by_key(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, unitOfWork) = await OpenAsync(dialect);
        using var _ = session;
        using (_toggle.UseMode(FilterMode.Raw))
            await session.InsertAsync(new Voucher { Code = "OTHER-1", TenantId = "other" });
        IBulkRepository<Voucher> vouchers = new Voucher.SqlRepository(unitOfWork);
        var (first, second) = (new Voucher { Code = "ACME-1" }, new Voucher { Code = "ACME-2" });

        Assert.Equal(2, await vouchers.BulkInsertAsync([first, second], CancellationToken.None));
        Assert.Equal(1, await vouchers.BulkDeleteAsync(Spec<Voucher>.Where(v => v.Code != "ACME-2"))); // ACME-1: OTHER-1 is another tenant's
        second.Code = "ACME-2B";
        Assert.Equal(2, await vouchers.BulkUpsertAsync([second, new Voucher { Code = "ACME-3" }], CancellationToken.None));

        List<Voucher> rows;
        using (_toggle.UseMode(FilterMode.Raw))
            rows = await session.Query<Voucher>().ToListAsync();
        Assert.Equal(["acme:ACME-2B", "acme:ACME-3", "other:OTHER-1"], rows.Select(v => $"{v.TenantId}:{v.Code}").OrderBy(v => v, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task An_update_the_generator_did_not_see_says_so()
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, unitOfWork) = await OpenAsync(SqlDialect.Sqlite);
        using var _ = session;
        IBulkRepository<Review> reviews = new Review.SqlRepository(unitOfWork);
        Func<ISpecification<Review>, Action<IBulkSetters<Review>>, CancellationToken, Task<int>> notACall = reviews.BulkUpdateAsync; // a method group: no call site to compile

        var refused = await Assert.ThrowsAsync<NotSupportedException>(() => notACall(Spec<Review>.Where(r => r.Stars > 0), s => s.SetProperty(r => r.Stars, 1), CancellationToken.None));

        Assert.Contains("compiled where it is called", refused.Message, StringComparison.Ordinal);
    }
}
