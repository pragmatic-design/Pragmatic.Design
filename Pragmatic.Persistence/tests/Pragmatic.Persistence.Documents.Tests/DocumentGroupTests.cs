using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Documents.Tests;

/// <summary>
///     A grouped chain on MongoDB: <c>Where</c>, <c>GroupBy</c>, a <c>Select</c> of the key and the aggregates into a new object, one
///     <c>aggregate</c> (<c>$match</c>, <c>$group</c>); the groups compared with LINQ over the same documents (an enum key and a Guid key).
/// </summary>
[Collection("mongo")]
public class DocumentGroupTests(MongoServer mongo)
{
    [Fact]
    public async Task A_group_by_answers_the_key_and_the_aggregates_of_each_group()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = new DocSession(mongo.Client, MongoServer.Database());
        var customers = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var orders = Enumerable.Range(0, 9).Select(i => new OrderDocument
        {
            Id = Guid.NewGuid(),
            CustomerId = customers[i % 2],
            Number = $"ORD-G{i}",
            Status = (OrderStatus)(i % 3),
            Total = 10m + i * 2.5m,
            CreatedAt = new DateTimeOffset(2026, 10, 20, 8, i, 0, TimeSpan.Zero),
            IsDeleted = i == 8,
        }).ToList();
        foreach (var order in orders)
            await session.InsertAsync(order);
        var live = orders.Where(o => !o.IsDeleted).ToList();

        var byCustomer = await session.Query<OrderDocument>().Where(o => !o.IsDeleted).GroupBy(o => o.CustomerId)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(o => o.Total), Lowest = g.Min(o => o.Total), Highest = g.Max(o => o.Total), Mean = g.Average(o => o.Total) }).ToListAsync();
        var expected = live.GroupBy(o => o.CustomerId).Select(g => (g.Key, g.Count(), g.Sum(o => o.Total), g.Min(o => o.Total), g.Max(o => o.Total), g.Average(o => o.Total))).OrderBy(g => g.Key).ToList();
        Assert.Equal(expected, byCustomer.Select(g => (g.Key, g.Count, g.Total, g.Lowest, g.Highest, g.Mean)).OrderBy(g => g.Key));

        var byStatus = await session.Query<OrderDocument>().Where(o => !o.IsDeleted).GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        Assert.Equal(live.GroupBy(o => o.Status).Select(g => (g.Key, g.Count())).OrderBy(g => g.Key), byStatus.Select(g => (g.Status, g.Count)).OrderBy(g => g.Status));
    }

    [Fact]
    public async Task Groups_are_filtered_ordered_and_paged_after_the_select()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = new DocSession(mongo.Client, MongoServer.Database());
        var orders = Enumerable.Range(0, 13).Select(i => new OrderDocument
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Number = $"ORD-H{i}",
            Status = (OrderStatus)(i % 3),
            Total = 10m + i,
            CreatedAt = new DateTimeOffset(2026, 10, 20, 9, i, 0, TimeSpan.Zero),
        }).ToList();
        foreach (var order in orders)
            await session.InsertAsync(order);
        var expected = orders.GroupBy(o => o.Status).Select(g => (Status: g.Key, Count: g.Count(), Total: g.Sum(o => o.Total))).ToList();

        // HAVING: a $match on the groups after $group, with a constant, with an argument, on the key.
        var rich = await session.Query<OrderDocument>().GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(o => o.Total) })
            .Where(g => g.Total > 60m).ToListAsync();
        Assert.Equal(expected.Where(g => g.Total > 60m).OrderBy(g => g.Status), rich.Select(g => (g.Status, g.Count, g.Total)).OrderBy(g => g.Status));
        var many = await session.Query<OrderDocument>().GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(o => o.Total) })
            .Where((g, least) => g.Count >= least && g.Status != OrderStatus.Paid, 5).ToListAsync();
        Assert.Equal(expected.Where(g => g.Count >= 5 && g.Status != OrderStatus.Paid), many.Select(g => (g.Status, g.Count, g.Total)));

        // The groups ordered by an aggregate, then paged: $sort, $skip, $limit after $group.
        var second = await session.Query<OrderDocument>().GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(o => o.Total) })
            .OrderByDescending(g => g.Total).Skip(1).Take(1).ToListAsync();
        Assert.Equal(expected.OrderByDescending(g => g.Total).Skip(1).Take(1), second.Select(g => (g.Status, g.Count, g.Total)));

        // Unordered groups come in the server's order: paging them is refused, not answered at random.
        await Assert.ThrowsAsync<NotSupportedException>(async () => await session.Query<OrderDocument>().GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count() }).Skip(1).ToListAsync());
    }
}
