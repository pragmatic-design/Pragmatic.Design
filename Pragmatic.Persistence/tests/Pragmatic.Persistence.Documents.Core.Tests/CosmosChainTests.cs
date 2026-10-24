using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     The same chains on Cosmos DB (the emulator), through the queries the generator rendered from the same conditions as MongoDB's filters.
///     In the customer's partition every form runs on the server; across partitions an unordered list and Any run, an ordering, a count or an
///     aggregate is refused with the remedy (the gateway does not serve them). Each answer compared with LINQ over the same documents.
/// </summary>
[Collection("cosmos")]
public class CosmosChainTests
{
    private readonly CosmosServer cosmos;

    public CosmosChainTests(CosmosServer server)
    {
        cosmos = server;
        // Once: BusyCustomer counts every customer's orders, so it is read once per test, not once per order filtered.
        Customer = server.Seed.BusyCustomer.Id;
        var customer = Customer;
        Mine = server.Orders.Where(o => o.CustomerId == customer).ToList();
    }

    private Guid Customer { get; }

    private List<OrderDocument> Mine { get; }

    [Fact]
    public async Task A_page_in_the_partition_reads_the_documents_whole()
    {
        if (cosmos.Unavailable is not null)
            return;
        var session = cosmos.Session();
        var expected = Mine.Where(o => o.Status == OrderStatus.Open && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).Skip(5).Take(10).ToList();
        Assert.Equal(10, expected.Count);
        var page = await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id && o.Status == OrderStatus.Open && !o.IsDeleted, Customer).OrderByDescending(o => o.CreatedAt).Skip(5).Take(10).ToListAsync();
        Assert.Equal(expected.Select(o => o.Id), page.Select(o => o.Id));
        var (source, first) = (expected[0], page[0]);
        Assert.Equal((source.Number, source.Status, source.Total, source.CreatedAt, source.IsDeleted, source.Version), (first.Number, first.Status, first.Total, first.CreatedAt, first.IsDeleted, first.Version));
        Assert.Equal(source.Lines.Select(l => (l.Id, l.Sku, l.Quantity, l.UnitPrice)), first.Lines.Select(l => (l.Id, l.Sku, l.Quantity, l.UnitPrice)));
        Assert.Equal(source.Skus, first.Skus);
        Assert.Equal((source.ShipTo?.Country, source.ShipTo?.City, source.ShipTo?.Floor), (first.ShipTo?.Country, first.ShipTo?.City, first.ShipTo?.Floor));
    }

    [Fact]
    public async Task Counts_firsts_and_aggregates_run_in_the_partition()
    {
        if (cosmos.Unavailable is not null)
            return;
        var session = cosmos.Session();
        Assert.Equal(Mine.Count(o => o.Status == OrderStatus.Open && !o.IsDeleted), await session.Query<OrderDocument>().CountAsync((o, id) => o.CustomerId == id && o.Status == OrderStatus.Open && !o.IsDeleted, Customer));
        Assert.True(await session.Query<OrderDocument>().AnyAsync((o, id) => o.CustomerId == id, Customer));
        Assert.False(await session.Query<OrderDocument>().AnyAsync((o, id) => o.CustomerId == id, Guid.NewGuid()));
        var some = Mine[3];
        Assert.Equal(some.Number, (await session.Query<OrderDocument>().FirstOrDefaultAsync((o, k) => o.CustomerId == k.Customer && o.Id == k.Id, (Customer, some.Id)))!.Number);
        var mine = session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, Customer);
        Assert.Equal(Mine.Max(o => o.Total), await mine.MaxAsync(o => o.Total));
        Assert.Equal(Mine.Min(o => o.CreatedAt), await mine.MinAsync(o => o.CreatedAt));
        Assert.Equal(Mine.Sum(o => o.Total), Math.Round(await mine.SumAsync(o => o.Total), 2)); // Cosmos DB sums doubles: exact to the cent here
    }

    [Fact]
    public async Task A_read_by_key_in_its_partition_is_a_point_read()
    {
        if (cosmos.Unavailable is not null)
            return;
        var session = cosmos.Session();
        var some = Mine[7];
        var read = await session.Query<OrderDocument>().FirstOrDefaultAsync((o, k) => o.CustomerId == k.Customer && o.Id == k.Id, (Customer, some.Id));
        Assert.Equal((some.Number, some.Status, some.Total, some.CreatedAt, some.Version), (read!.Number, read.Status, read.Total, read.CreatedAt, read.Version));
        Assert.Equal(some.Lines.Select(l => (l.Id, l.Sku, l.Quantity, l.UnitPrice)), read.Lines.Select(l => (l.Id, l.Sku, l.Quantity, l.UnitPrice)));
        Assert.Null(await session.Query<OrderDocument>().FirstOrDefaultAsync((o, k) => o.CustomerId == k.Customer && o.Id == k.Id, (Customer, Id: Guid.NewGuid())));
        Assert.Null(await session.Query<OrderDocument>().FirstOrDefaultAsync((o, k) => o.CustomerId == k.Customer && o.Id == k.Id, (Customer: Guid.NewGuid(), some.Id))); // another partition

        // A condition beyond the two keys is a query: the document that does not meet it is not returned.
        var other = some.Status == OrderStatus.Open ? OrderStatus.Paid : OrderStatus.Open;
        Assert.Null(await session.Query<OrderDocument>().FirstOrDefaultAsync((o, k) => o.CustomerId == k.Customer && o.Id == k.Id && o.Status == k.Status, (Customer, some.Id, Status: other)));
        Assert.Equal(some.Number, await session.Query<OrderDocument>().Where((o, k) => o.Id == k.Id && o.CustomerId == k.Customer, (Customer, some.Id)).Select(o => o.Number).FirstOrDefaultAsync());
    }

    [Fact]
    public async Task Projections_keyset_and_embedded_conditions_run_in_the_partition()
    {
        if (cosmos.Unavailable is not null)
            return;
        var session = cosmos.Session();
        var expected = Mine.OrderBy(o => o.Number, StringComparer.Ordinal).Take(5).Select(o => (o.Number, o.Total)).ToList();
        var rows = await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, Customer).OrderBy(o => o.Number).Take(5).Select(o => new { o.Number, o.Total }).ToListAsync();
        Assert.Equal(expected, rows.Select(r => (r.Number, r.Total)));

        var ordered = session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, Customer).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id);
        var first = await ordered.Take(7).ToListAsync();
        Assert.Equal((await ordered.Skip(7).Take(7).ToListAsync()).Select(o => o.Id), (await ordered.After((first[^1].CreatedAt, first[^1].Id)).Take(7).ToListAsync()).Select(o => o.Id));

        Assert.Equal(Mine.Count(o => o.Lines.Any(l => l.Quantity > 5)), await session.Query<OrderDocument>().CountAsync((o, id) => o.CustomerId == id && o.Lines.Any(l => l.Quantity > 5), Customer));
        Assert.Equal(Mine.Count(o => o.Skus.Contains("BOLT")), await session.Query<OrderDocument>().CountAsync((o, k) => o.CustomerId == k.Customer && o.Skus.Any(s => s == k.Sku), (Customer, Sku: "BOLT")));
        Assert.Equal(Mine.Count(o => o.ShipTo == null), await session.Query<OrderDocument>().CountAsync((o, id) => o.CustomerId == id && o.ShipTo == null, Customer));
        Assert.Equal(Mine.Count(o => o.ShipTo?.Floor > 3), await session.Query<OrderDocument>().CountAsync((o, id) => o.CustomerId == id && o.ShipTo!.Floor > 3, Customer));
        Assert.Equal(Mine.Count(o => o.Number.Contains("1", StringComparison.Ordinal)), await session.Query<OrderDocument>().CountAsync((o, id) => o.CustomerId == id && o.Number.Contains("1"), Customer));
    }

    [Fact]
    public async Task Across_partitions_filters_counts_and_orderings_run_and_the_rest_is_refused_with_the_remedy()
    {
        if (cosmos.Unavailable is not null)
            return;
        var session = cosmos.Session();
        var countries = new List<string> { "IT", "DE" };
        var customers = await session.Query<CustomerDocument>().Where((c, list) => list.Contains(c.Country), countries).ToListAsync();
        Assert.Equal(cosmos.Customers.Where(c => countries.Contains(c.Country)).Select(c => c.Id).Order(), customers.Select(c => c.Id).Order());
        var early = await session.Query<OrderDocument>().Where(o => o.Number.StartsWith("ORD-1000")).ToListAsync();
        Assert.Equal(cosmos.Orders.Where(o => o.Number.StartsWith("ORD-1000", StringComparison.Ordinal)).Select(o => o.Id).Order(), early.Select(o => o.Id).Order());
        Assert.Equal(3, (await session.Query<OrderDocument>().Where(o => !o.IsDeleted).Take(3).ToListAsync()).Count);
        Assert.True(await session.Query<OrderDocument>().AnyAsync(o => o.Lines.Any(l => l.Sku == "GEAR")));

        // An ordered page across partitions is read range by range and merged here (owner's decision of 2026-10-22), as the SDKs do.
        var ordered = await session.Query<OrderDocument>().OrderByDescending(o => o.Number).Skip(3).Take(5).ToListAsync();
        Assert.Equal(cosmos.Orders.Select(o => o.Number).OrderDescending(StringComparer.Ordinal).Skip(3).Take(5), ordered.Select(o => o.Number));
        // A count across partitions is sent to each partition key range and added up (owner's decision of 2026-10-22), as the SDKs do.
        Assert.Equal(cosmos.Orders.Count(o => !o.IsDeleted), await session.Query<OrderDocument>().CountAsync(o => !o.IsDeleted));
        await Assert.ThrowsAsync<NotSupportedException>(async () => await session.Query<OrderDocument>().MaxAsync(o => o.Total));
        // No delete or update by filter: Cosmos DB writes a document by its id.
        var command = await Assert.ThrowsAsync<NotSupportedException>(async () => await session.Query<OrderDocument>().Where(o => o.IsDeleted).ExecuteDeleteAsync());
        Assert.Contains("by its id", command.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<NotSupportedException>(async () => await session.Query<OrderDocument>().GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync());
    }

    [Fact]
    public async Task Writes_check_the_version_through_the_etag()
    {
        if (cosmos.Unavailable is not null)
            return;
        var session = cosmos.Session();
        var order = new OrderDocument { Id = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Number = "ORD-C1", Status = OrderStatus.Open, Total = 10.5m, CreatedAt = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), Lines = [new LineDocument { Id = Guid.NewGuid(), Sku = "NUT", Quantity = 2, UnitPrice = 5.25m }] };
        await session.InsertAsync(order);
        var read = (await session.Query<OrderDocument>().FirstOrDefaultAsync((o, k) => o.CustomerId == k.Customer && o.Id == k.Id, (Customer: order.CustomerId, order.Id)))!;
        read.Total = 99m;
        await session.ReplaceAsync(read);
        Assert.Equal(1, read.Version);
        await Assert.ThrowsAsync<DocConcurrencyException>(() => session.ReplaceAsync(order)); // the copy at version 0 is stale
        Assert.Equal(0, order.Version);
        await Assert.ThrowsAsync<DocConcurrencyException>(() => session.DeleteAsync(order));
        read.Status = OrderStatus.Paid;
        await session.UpsertAsync(read);
        Assert.Equal(2, read.Version);
        var stored = (await session.Query<OrderDocument>().FirstOrDefaultAsync((o, k) => o.CustomerId == k.Customer && o.Id == k.Id, (Customer: order.CustomerId, order.Id)))!;
        Assert.Equal((99m, OrderStatus.Paid, 2), (stored.Total, stored.Status, stored.Version));
        await session.DeleteAsync(stored);
        Assert.False(await session.Query<OrderDocument>().AnyAsync((o, id) => o.CustomerId == id, order.CustomerId));
    }

    [Fact]
    public async Task SelectMany_joins_the_embedded_lines_in_the_partition()
    {
        if (cosmos.Unavailable is not null)
            return;
        var session = cosmos.Session();
        var lines = Mine.OrderBy(o => o.Number, StringComparer.Ordinal).SelectMany(o => o.Lines).ToList();
        var page = await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, Customer).OrderBy(o => o.Number).SelectMany(o => o.Lines).Skip(3).Take(10).ToListAsync();
        Assert.Equal(10, page.Count);
        Assert.Equal(lines.Skip(3).Take(10).Select(l => (l.Id, l.Sku, l.Quantity, l.UnitPrice)), page.Select(l => (l.Id, l.Sku, l.Quantity, l.UnitPrice)));
        Assert.Equal(lines.Count, (await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, Customer).SelectMany(o => o.Lines).ToListAsync()).Count);
    }

    [Fact]
    public async Task A_join_with_another_container_reads_the_inner_documents_of_the_keys()
    {
        if (cosmos.Unavailable is not null)
            return;
        var session = cosmos.Session();
        var country = cosmos.Customers.First(c => c.Id == Customer).Country;
        var expected = Mine.OrderBy(o => o.Number, StringComparer.Ordinal)
            .Join(cosmos.Customers.Where(c => c.Country == country), o => o.CustomerId, c => c.Id, (o, c) => (o.Number, c.Name)).ToList();
        Assert.Equal(Mine.Count, expected.Count);
        var rows = await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, Customer).OrderBy(o => o.Number)
            .Join(session.Query<CustomerDocument>().Where((c, k) => c.Country == k, country), o => o.CustomerId, c => c.Id, (o, c) => new { o.Number, c.Name })
            .Skip(3).Take(10).ToListAsync();
        Assert.Equal(expected.Skip(3).Take(10), rows.Select(r => (r.Number, r.Name)));
        Assert.Empty(await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, Customer)
            .Join(session.Query<CustomerDocument>().Where((c, k) => c.Country == k, "nowhere"), o => o.CustomerId, c => c.Id, (o, c) => new { o.Number, c.Name }).ToListAsync());
    }
}
