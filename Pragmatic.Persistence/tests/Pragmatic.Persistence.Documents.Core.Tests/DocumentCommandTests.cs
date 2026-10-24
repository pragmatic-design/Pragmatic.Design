using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     Commands by filter on MongoDB: an update of every document the chain selects (<c>$set</c> from a constant and the argument, the version
///     incremented, so a copy read before is stale) and a delete, each one command answering the count; the other customer's document untouched.
/// </summary>
[Collection("mongo")]
public class DocumentCommandTests(MongoServer mongo)
{
    [Fact]
    public async Task An_update_and_a_delete_by_filter_write_every_document_the_chain_selects()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = new DocSession(mongo.Client, MongoServer.Database());
        var customer = Guid.NewGuid();
        var mine = Enumerable.Range(1, 3).Select(i => New(customer, $"ORD-C{i}")).ToList();
        var other = New(Guid.NewGuid(), "ORD-X");
        foreach (var order in mine.Append(other))
            await session.InsertAsync(order);

        var updated = await session.Query<OrderDocument>().Where((o, c) => o.CustomerId == c, customer)
            .ExecuteUpdateAsync((s, total) => s.Set(o => o.Status, OrderStatus.Paid).Set(o => o.Total, total), 99.5m);
        Assert.Equal(3, updated);
        var read = await session.Query<OrderDocument>().Where((o, c) => o.CustomerId == c, customer).ToListAsync();
        Assert.All(read, o => Assert.Equal((OrderStatus.Paid, 99.5m, 1), (o.Status, o.Total, o.Version)));
        await Assert.ThrowsAsync<DocConcurrencyException>(() => session.ReplaceAsync(mine[0])); // read before the update: stale
        var untouched = (await session.Query<OrderDocument>().FirstOrDefaultAsync((o, id) => o.Id == id, other.Id))!;
        Assert.Equal((OrderStatus.Open, 39.98m, 0), (untouched.Status, untouched.Total, untouched.Version));

        Assert.Equal(0, await session.Query<OrderDocument>().Where(o => o.Status == OrderStatus.Cancelled).ExecuteDeleteAsync());
        Assert.Equal(3, await session.Query<OrderDocument>().Where((o, c) => o.CustomerId == c && o.Status == OrderStatus.Paid, customer).ExecuteDeleteAsync());
        Assert.Equal(1, await session.Query<OrderDocument>().CountAsync());
        await Assert.ThrowsAsync<NotSupportedException>(async () => await session.Query<OrderDocument>().Take(1).ExecuteDeleteAsync());
    }

    private static OrderDocument New(Guid customer, string number) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = customer,
        Number = number,
        Status = OrderStatus.Open,
        Total = 39.98m,
        CreatedAt = new DateTimeOffset(2026, 10, 20, 8, 30, 0, TimeSpan.Zero),
        Lines = [new LineDocument { Id = Guid.NewGuid(), Sku = "BOLT", Quantity = 2, UnitPrice = 19.99m }],
        Skus = ["BOLT"],
    };
}
