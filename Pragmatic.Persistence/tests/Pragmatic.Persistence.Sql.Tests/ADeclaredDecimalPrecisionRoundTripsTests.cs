using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A decimal declared <c>[Precision(18, 4)]</c> keeps its four decimals on every database (#731).
/// </summary>
/// <remarks>
///     Every decimal was <c>numeric(18,2)</c>, and its parameter was typed (18,2) too: the column rounded the value, and on SQL Server the
///     parameter rounded it before it got there. <see cref="Archive.Rate" /> is now <c>numeric(18,4)</c>, and the parameter carries the
///     value's scale.
/// </remarks>
[Collection("pragmatic databases")]
public class ADeclaredDecimalPrecisionRoundTripsTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_rate_with_four_decimals_is_read_back_whole(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));

        var archive = new Archive { Body = "rate", Scan = [1], Rate = 1.2345m };
        await session.InsertAsync(archive);

        var stored = (await session.Query<Archive>().ToListAsync()).Single(d => d.Id == archive.Id);
        Assert.Equal(1.2345m, stored.Rate);
    }

    /// <summary>Found by its whole value: a filter's parameter keeps the four decimals too.</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_rate_is_found_by_its_four_decimals(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));

        var archive = new Archive { Body = "found", Scan = [2], Rate = 7.0625m };
        await session.InsertAsync(archive);

        var found = await session.Query<Archive>().Where((d, value) => d.Rate == value, 7.0625m).ToListAsync();

        Assert.Contains(found, d => d.Id == archive.Id);
    }
}
