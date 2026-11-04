using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A one-to-one read from its principal side on the five databases (#503): <c>Desk.Lamp</c> joins on the lamp's <c>DeskId</c>, the key the
///     desk does not hold. A desk without a lamp reads null, through an include and through a projection: the join is LEFT.
/// </summary>
[Collection("pragmatic databases")]
public class PrincipalOneToOneTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<SqlSession> SeededAsync(SqlDialect dialect)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        var (lit, dark) = (new Desk { Code = "D-1" }, new Desk { Code = "D-2" });
        await session.InsertAsync(lit);
        await session.InsertAsync(dark);
        var lamp = new DeskLamp { Watts = 40 };
        lamp.SetDeskId(lit.Id);
        await session.InsertAsync(lamp);
        return session;
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_principal_includes_its_dependent_and_reads_null_without_one(SqlDialect dialect)
    {
        using var session = await SeededAsync(dialect);

        var desks = await session.Query<Desk>().Include(d => d.Lamp).OrderBy(d => d.Code).ToListAsync();

        Assert.Equal(["D-1", "D-2"], desks.Select(d => d.Code));
        Assert.Equal(40, desks[0].Lamp.Watts);
        Assert.Equal(desks[0].Id, desks[0].Lamp.DeskId);
        Assert.Null(desks[1].Lamp);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_principal_projects_its_dependents_field_and_filters_on_it(SqlDialect dialect)
    {
        using var session = await SeededAsync(dialect);

        var all = await session.Query<Desk>().OrderBy(d => d.Code).Select(d => new { d.Code, Watts = (int?)d.Lamp.Watts }).ToListAsync();
        var bright = await session.Query<Desk>().Where(d => d.Lamp.Watts > 10).Select(d => d.Code).ToListAsync();

        Assert.Equal([("D-1", (int?)40), ("D-2", null)], all.Select(d => (d.Code, d.Watts)));
        Assert.Equal(["D-1"], bright);
    }
}
