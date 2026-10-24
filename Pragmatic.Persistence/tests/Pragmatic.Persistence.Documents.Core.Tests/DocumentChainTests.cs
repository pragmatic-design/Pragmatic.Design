using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     The chains on <c>DocQuery&lt;T&gt;</c>, translated at compile time into MongoDB queries, against the seeded workload: each answer compared
///     with LINQ over the same documents in memory. The acceptance list of the plan: a filtered, ordered page; Count, Any, FirstOrDefault by
///     key; Max, Min, Sum; a projection; a list as <c>$in</c>; Contains and StartsWith; keyset pages; Any on the embedded lines and on an array
///     of values; a path into an embedded document and its absence.
/// </summary>
[Collection("mongo")]
public class DocumentChainTests(MongoServer mongo)
{
    [Fact]
    public async Task A_filtered_ordered_page_reads_the_documents_whole()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = mongo.Session();
        var customer = mongo.Seed.BusyCustomer.Id;
        var expected = mongo.Orders.Where(o => o.CustomerId == customer && o.Status == OrderStatus.Open && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).Skip(5).Take(10).ToList();
        var page = await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id && o.Status == OrderStatus.Open && !o.IsDeleted, customer).OrderByDescending(o => o.CreatedAt).Skip(5).Take(10).ToListAsync();
        Assert.Equal(10, expected.Count); // not an empty page compared with an empty page
        Assert.Equal(expected.Select(o => o.Id), page.Select(o => o.Id));
        var first = page[0];
        var source = expected[0];
        Assert.Equal((source.Number, source.Status, source.Total, source.CreatedAt, source.IsDeleted), (first.Number, first.Status, first.Total, first.CreatedAt, first.IsDeleted));
        Assert.Equal(source.Lines.Select(l => (l.Id, l.Sku, l.Quantity, l.UnitPrice)), first.Lines.Select(l => (l.Id, l.Sku, l.Quantity, l.UnitPrice)));
        Assert.Equal(source.Skus, first.Skus);
        Assert.Equal((source.ShipTo?.Country, source.ShipTo?.City, source.ShipTo?.Floor), (first.ShipTo?.Country, first.ShipTo?.City, first.ShipTo?.Floor));
    }

    [Fact]
    public async Task Count_any_first_and_the_aggregates_answer_what_linq_answers()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = mongo.Session();
        var customer = mongo.Seed.BusyCustomer.Id;
        var mine = mongo.Orders.Where(o => o.CustomerId == customer).ToList();
        Assert.Equal(mine.Count(o => o.Status == OrderStatus.Open && !o.IsDeleted), await session.Query<OrderDocument>().CountAsync((o, id) => o.CustomerId == id && o.Status == OrderStatus.Open && !o.IsDeleted, customer));
        Assert.Equal(mongo.Orders.Count, await session.Query<OrderDocument>().CountAsync());
        Assert.True(await session.Query<OrderDocument>().AnyAsync((o, id) => o.CustomerId == id, customer));
        Assert.False(await session.Query<OrderDocument>().AnyAsync((o, id) => o.CustomerId == id, Guid.NewGuid()));
        var some = mongo.Orders[1234];
        Assert.Equal(some.Number, (await session.Query<OrderDocument>().FirstOrDefaultAsync((o, id) => o.Id == id, some.Id))!.Number);
        Assert.Null(await session.Query<OrderDocument>().FirstOrDefaultAsync((o, id) => o.Id == id, Guid.NewGuid()));
        var theirs = session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, customer);
        Assert.Equal(mine.Max(o => o.Total), await theirs.MaxAsync(o => o.Total));
        Assert.Equal(mine.Min(o => o.CreatedAt), await theirs.MinAsync(o => o.CreatedAt));
        Assert.Equal(mine.Sum(o => o.Total), await theirs.SumAsync(o => o.Total));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, Guid.NewGuid()).MaxAsync(o => o.Total));
    }

    [Fact]
    public async Task A_projection_reads_the_fields_it_names()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = mongo.Session();
        var customer = mongo.Seed.BusyCustomer.Id;
        var expected = mongo.Orders.Where(o => o.CustomerId == customer).OrderBy(o => o.Number, StringComparer.Ordinal).Take(5).Select(o => (o.Number, o.Total)).ToList();
        var rows = await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, customer).OrderBy(o => o.Number).Take(5).Select(o => new { o.Number, o.Total }).ToListAsync();
        Assert.Equal(expected, rows.Select(r => (r.Number, r.Total)));
        var numbers = await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, customer).OrderBy(o => o.Number).Take(3).Select(o => o.Number).ToListAsync();
        Assert.Equal(expected.Take(3).Select(e => e.Number), numbers);
    }

    [Fact]
    public async Task A_list_is_in_and_text_is_matched_as_it_is()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = mongo.Session();
        var countries = new List<string> { "IT", "DE" };
        Assert.InRange(mongo.Customers.Count(c => countries.Contains(c.Country)), 1, mongo.Customers.Count - 1);
        Assert.Equal(mongo.Customers.Count(c => countries.Contains(c.Country)), await session.Query<CustomerDocument>().CountAsync((c, list) => list.Contains(c.Country), countries));
        var text = mongo.Orders[1234].Number.Substring(4, 3);
        Assert.InRange(mongo.Orders.Count(o => o.Number.Contains(text, StringComparison.Ordinal) && !o.IsDeleted), 1, mongo.Orders.Count - 1);
        Assert.Equal(mongo.Orders.Count(o => o.Number.Contains(text, StringComparison.Ordinal) && !o.IsDeleted), await session.Query<OrderDocument>().CountAsync((o, t) => o.Number.Contains(t) && !o.IsDeleted, text));
        Assert.Equal(mongo.Orders.Count(o => o.Number.StartsWith("ORD-1000", StringComparison.Ordinal)), await session.Query<OrderDocument>().CountAsync(o => o.Number.StartsWith("ORD-1000")));
        Assert.Equal(0, await session.Query<OrderDocument>().CountAsync((o, t) => o.Number.Contains(t), "ORD-1.*")); // a regex metacharacter matched as itself
    }

    [Fact]
    public async Task Keyset_pages_are_the_pages_offset_gives()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = mongo.Session();
        var ordered = session.Query<OrderDocument>().Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id);
        var first = await ordered.Take(20).ToListAsync();
        var after = await ordered.After((first[^1].CreatedAt, first[^1].Id)).Take(20).ToListAsync();
        var byOffset = await ordered.Skip(20).Take(20).ToListAsync();
        Assert.Equal(byOffset.Select(o => o.Id), after.Select(o => o.Id));
        var descending = session.Query<OrderDocument>().OrderByDescending(o => o.Total).ThenBy(o => o.Id);
        var top = await descending.Take(15).ToListAsync();
        Assert.Equal((await descending.Skip(15).Take(15).ToListAsync()).Select(o => o.Id), (await descending.After((top[^1].Total, top[^1].Id)).Take(15).ToListAsync()).Select(o => o.Id));
    }

    [Fact]
    public async Task Embedded_documents_and_arrays_are_queried_in_place()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = mongo.Session();
        // Every expected count is neither zero nor all: a condition that matched nothing, or everything, would pass without proving it was applied.
        Assert.All(new[] { mongo.Orders.Count(o => o.Lines.Any(l => l.Quantity > 8 && l.Sku == "GEAR")), mongo.Orders.Count(o => o.Skus.Contains("BOLT")), mongo.Orders.Count(o => o.ShipTo == null), mongo.Orders.Count(o => o.ShipTo?.Country == "IT"), mongo.Orders.Count(o => o.ShipTo?.Floor > 3) },
            n => Assert.InRange(n, 1, mongo.Orders.Count - 1));
        Assert.Equal(mongo.Orders.Count(o => o.Lines.Any(l => l.Quantity > 8 && l.Sku == "GEAR")), await session.Query<OrderDocument>().CountAsync(o => o.Lines.Any(l => l.Quantity > 8 && l.Sku == "GEAR")));
        Assert.Equal(mongo.Orders.Count(o => o.Skus.Any(s => s == "BOLT")), await session.Query<OrderDocument>().CountAsync((o, sku) => o.Skus.Any(s => s == sku), "BOLT"));
        Assert.Equal(mongo.Orders.Count(o => o.ShipTo == null), await session.Query<OrderDocument>().CountAsync(o => o.ShipTo == null));
        Assert.Equal(mongo.Orders.Count(o => o.ShipTo != null && o.ShipTo.Country == "IT"), await session.Query<OrderDocument>().CountAsync(o => o.ShipTo!.Country == "IT"));
        Assert.Equal(mongo.Orders.Count(o => o.ShipTo != null && o.ShipTo.Floor > 3), await session.Query<OrderDocument>().CountAsync(o => o.ShipTo!.Floor > 3));
        Assert.Equal(mongo.Orders.Count(o => o.Lines.Count > 0), await session.Query<OrderDocument>().CountAsync(o => o.Lines.Any()));
    }

    [Fact]
    public async Task SelectMany_flattens_the_embedded_lines_in_the_documents_order()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = mongo.Session();
        var customer = mongo.Seed.BusyCustomer.Id;
        var lines = mongo.Orders.Where(o => o.CustomerId == customer).OrderBy(o => o.Number, StringComparer.Ordinal).SelectMany(o => o.Lines).ToList();
        var page = await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, customer).OrderBy(o => o.Number).SelectMany(o => o.Lines).Skip(3).Take(10).ToListAsync();
        Assert.Equal(10, page.Count);
        Assert.Equal(lines.Skip(3).Take(10).Select(l => (l.Id, l.Sku, l.Quantity, l.UnitPrice)), page.Select(l => (l.Id, l.Sku, l.Quantity, l.UnitPrice)));
        Assert.Equal(lines.Count, (await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, customer).SelectMany(o => o.Lines).ToListAsync()).Count);
    }

    [Fact]
    public async Task A_join_with_another_collection_is_a_lookup()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = mongo.Session();
        var country = mongo.Customers[0].Country;
        var expected = mongo.Orders.Where(o => o.Total > 1000m).OrderBy(o => o.Number, StringComparer.Ordinal)
            .Join(mongo.Customers.Where(c => c.Country == country), o => o.CustomerId, c => c.Id, (o, c) => (o.Number, c.Name)).ToList();
        Assert.True(expected.Count > 17, $"the data gives {expected.Count} joined rows: too few for a page"); // not an empty page compared with an empty page
        var rows = await session.Query<OrderDocument>().Where(o => o.Total > 1000m).OrderBy(o => o.Number)
            .Join(session.Query<CustomerDocument>().Where((c, k) => c.Country == k, country), o => o.CustomerId, c => c.Id, (o, c) => new { o.Number, c.Name })
            .Skip(2).Take(15).ToListAsync();
        Assert.Equal(expected.Skip(2).Take(15), rows.Select(r => (r.Number, r.Name)));
    }
}
