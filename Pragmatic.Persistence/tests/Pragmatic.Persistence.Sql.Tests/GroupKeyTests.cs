using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     A composite GROUP BY key: an anonymous object or a tuple of columns (through navigations too); after it <c>g.Key.Member</c>
///     is one column, <c>g.Key</c> the whole key rebuilt, in the projection, the HAVING and the ordering.
/// </summary>
[Collection("databases")]
public class GroupKeyTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void A_composite_key_groups_by_every_column_and_reads_its_members()
    {
        var byCustomerAndStatus = Session(SqlDialect.PostgreSql).Query<Order>().Where(o => !o.IsDeleted)
            .GroupBy(o => new { o.CustomerId, o.Status })
            .Select(g => new { g.Key.CustomerId, g.Key.Status, Orders = g.Count(), Total = g.Sum(o => o.Total) });
        Assert.Equal("SELECT t.\"CustomerId\", t.\"Status\", COUNT(*), SUM(t.\"Total\") FROM \"Orders\" t WHERE (NOT (t.\"IsDeleted\" = @p0)) GROUP BY t.\"CustomerId\", t.\"Status\"", byCustomerAndStatus.ToSql());

        // The whole key rebuilt, a HAVING on an aggregate and an ordering on a member of the key; a tuple key with its element names.
        var tuple = Session(SqlDialect.SqlServer).Query<Order>().GroupBy(o => (o.Customer!.Country, o.Status))
            .Where((g, atLeast) => g.Count() >= atLeast, 2)
            .OrderBy(g => g.Key.Country).ThenByDescending(g => g.Key.Item2)
            .Select(g => new { Key = g.Key, Orders = g.Count() }).Take(10);
        Assert.Equal("SELECT j_Customer.[Country], t.[Status], COUNT(*) FROM [Orders] t INNER JOIN [Customers] j_Customer ON j_Customer.[Id] = t.[CustomerId] GROUP BY j_Customer.[Country], t.[Status] HAVING (COUNT(*) >= @p0) ORDER BY j_Customer.[Country], t.[Status] DESC OFFSET 0 ROWS FETCH NEXT @p1 ROWS ONLY", tuple.ToSql());

        // A DTO result, one member of the key read into a property.
        var dto = Session(SqlDialect.Sqlite).Query<Order>().GroupBy(o => new { o.Status, o.IsDeleted }).Select(g => new TranslationTests.StatusCount { Status = g.Key.Status, Count = g.Count() });
        Assert.Equal("SELECT t.\"Status\", COUNT(*) FROM \"Orders\" t GROUP BY t.\"Status\", t.\"IsDeleted\"", dto.ToSql());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_composite_key_computes_the_groups_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var customers = seed.Customers.ToDictionary(c => c.Id);

        var expected = seed.Orders.Where(o => !o.IsDeleted).GroupBy(o => (o.CustomerId, o.Status)).Select(g => (g.Key.CustomerId, g.Key.Status, Orders: g.Count(), Total: g.Sum(o => o.Total))).OrderBy(g => g.CustomerId).ThenBy(g => g.Status).ToList();
        var groups = await session.Query<Order>().Where(o => !o.IsDeleted).GroupBy(o => new { o.CustomerId, o.Status })
            .OrderBy(g => g.Key.CustomerId).ThenBy(g => g.Key.Status)
            .Select(g => new { g.Key.CustomerId, g.Key.Status, Orders = g.Count(), Total = g.Sum(o => o.Total) }).ToListAsync();
        Assert.Equal(expected, groups.Select(g => (g.CustomerId, g.Status, g.Orders, g.Total)).OrderBy(g => g.CustomerId).ThenBy(g => g.Status)); // SQL Server orders uniqueidentifiers by their own byte order: the sets are compared in .NET's

        // The whole key through a navigation, filtered on the groups: the tuple comes back as declared.
        var byCountry = seed.Orders.GroupBy(o => (customers[o.CustomerId].Country, o.Status)).Where(g => g.Count() >= 3).Select(g => (g.Key.Country, g.Key.Status, Orders: g.Count())).OrderBy(g => g.Country, StringComparer.Ordinal).ThenBy(g => g.Status).ToList();
        var rows = await session.Query<Order>().GroupBy(o => (o.Customer!.Country, o.Status)).Where((g, atLeast) => g.Count() >= atLeast, 3)
            .OrderBy(g => g.Key.Country).ThenBy(g => g.Key.Status).Select(g => new { Key = g.Key, Orders = g.Count() }).ToListAsync();
        Assert.Equal(byCountry, rows.Select(r => (r.Key.Country, r.Key.Status, r.Orders)));
        Assert.Equal(byCountry.Count, rows.Count);
    }
}
