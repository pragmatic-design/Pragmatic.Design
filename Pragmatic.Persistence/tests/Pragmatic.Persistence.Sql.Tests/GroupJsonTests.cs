using System.Text.Json;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     <c>Sql.JsonArray(g.Select(x => col))</c> in a grouped projection: each group's values as one JSON array (strings quoted and escaped,
///     numbers as numbers), the same elements as the seed's on the five databases; the order of the elements is the database's.
/// </summary>
[Collection("databases")]
public class GroupJsonTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_group_aggregates_its_values_into_a_json_array(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        var seed = databases.Seed;
        var customerId = seed.BusyCustomer.Id;
        var orders = seed.Orders.Where(o => o.CustomerId == customerId).Select(o => o.Id).ToHashSet();
        using var session = databases.Session(dialect);

        var groups = await session.Query<OrderLine>().Where((l, id) => l.Order!.CustomerId == id, customerId).GroupBy(l => l.OrderId)
            .Select(g => new { Order = g.Key, Skus = Sql.JsonArray(g.Select(l => l.Sku)), Quantities = Sql.JsonArray(g.Select(l => l.Quantity)) }).ToListAsync();
        Assert.Equal(orders.Count, groups.Count);
        foreach (var group in groups)
        {
            var lines = seed.Lines.Where(l => l.OrderId == group.Order).ToList();
            using var skus = JsonDocument.Parse(group.Skus!);
            using var quantities = JsonDocument.Parse(group.Quantities!);
            Assert.Equal(lines.Select(l => l.Sku).Order(StringComparer.Ordinal), skus.RootElement.EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.Ordinal));
            Assert.Equal(lines.Select(l => l.Quantity).Order(), quantities.RootElement.EnumerateArray().Select(e => e.GetInt32()).Order());
        }
    }
}
