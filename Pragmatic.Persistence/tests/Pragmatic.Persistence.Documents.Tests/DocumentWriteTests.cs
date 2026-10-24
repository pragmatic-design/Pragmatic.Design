using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Documents.Tests;

/// <summary>
///     Writes of whole documents: an insert read back with its embedded lines, a replace at the version the object holds (and one more after
///     it), a stale copy refused on replace, upsert and delete, an upsert that inserts, a duplicate insert refused by the server.
/// </summary>
[Collection("mongo")]
public class DocumentWriteTests(MongoServer mongo)
{
    [Fact]
    public async Task A_replace_checks_and_increments_the_version()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = new DocSession(mongo.Client, MongoServer.Database());
        var order = New("ORD-W1");
        await session.InsertAsync(order);
        Assert.Equal(0, order.Version);

        var read = (await session.Query<OrderDocument>().FirstOrDefaultAsync((o, id) => o.Id == id, order.Id))!;
        Assert.Equal(("ORD-W1", 2, 19.99m), (read.Number, read.Lines.Count, read.Lines[1].UnitPrice));
        read.Total = 42m;
        read.Lines.Add(new LineDocument { Id = Guid.NewGuid(), Sku = "NUT", Quantity = 3, UnitPrice = 1.5m });
        await session.ReplaceAsync(read);
        Assert.Equal(1, read.Version);

        // The copy read before the replace is stale: refused, and it keeps the version it had.
        await Assert.ThrowsAsync<DocConcurrencyException>(() => session.ReplaceAsync(order));
        Assert.Equal(0, order.Version);
        await Assert.ThrowsAsync<DocConcurrencyException>(() => session.UpsertAsync(order));
        await Assert.ThrowsAsync<DocConcurrencyException>(() => session.DeleteAsync(order));

        var stored = (await session.Query<OrderDocument>().FirstOrDefaultAsync((o, id) => o.Id == id, order.Id))!;
        Assert.Equal((42m, 3, 1), (stored.Total, stored.Lines.Count, stored.Version));

        // The fresh copy upserts (a replace at its version), then deletes.
        stored.Status = OrderStatus.Paid;
        await session.UpsertAsync(stored);
        Assert.Equal(2, stored.Version);
        Assert.Equal(OrderStatus.Paid, (await session.Query<OrderDocument>().FirstOrDefaultAsync((o, id) => o.Id == id, order.Id))!.Status);
        await session.DeleteAsync(stored);
        Assert.False(await session.Query<OrderDocument>().AnyAsync((o, id) => o.Id == id, order.Id));
    }

    [Fact]
    public async Task An_upsert_of_a_new_key_inserts_and_a_duplicate_insert_is_refused()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = new DocSession(mongo.Client, MongoServer.Database());
        var order = New("ORD-W2");
        await session.UpsertAsync(order);
        Assert.Equal(1, order.Version); // every successful write of a versioned document moves its version
        Assert.Equal(1, await session.Query<OrderDocument>().CountAsync());
        var duplicate = await Assert.ThrowsAsync<MongoWriteException>(() => session.InsertAsync(order));
        Assert.True(duplicate.IsDuplicateKey);
    }

    [Fact]
    public async Task A_document_without_a_version_is_replaced_and_deleted_by_its_key()
    {
        if (mongo.Unavailable is not null)
            return;
        var session = new DocSession(mongo.Client, MongoServer.Database());
        var customer = new CustomerDocument { Id = Guid.NewGuid(), Name = "Ada", Email = "ada@example.test", Country = "IT", CreatedAt = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero) };
        await session.InsertAsync(customer);
        customer.Email = "ada@new.test";
        await session.ReplaceAsync(customer);
        Assert.Equal("ada@new.test", (await session.Query<CustomerDocument>().FirstOrDefaultAsync((c, id) => c.Id == id, customer.Id))!.Email);
        await session.DeleteAsync(customer);
        await Assert.ThrowsAsync<DocConcurrencyException>(() => session.ReplaceAsync(customer)); // gone: nothing to replace
    }

    [Fact]
    public async Task A_standalone_server_says_it_runs_no_transactions()
    {
        if (mongo.Unavailable is not null)
            return;
        Assert.False(await new DocSession(mongo.Client, MongoServer.Database()).SupportsTransactionsAsync());
    }

    private static OrderDocument New(string number) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        Number = number,
        Status = OrderStatus.Open,
        Total = 39.98m,
        CreatedAt = new DateTimeOffset(2026, 10, 20, 8, 30, 0, TimeSpan.Zero),
        Lines = [new LineDocument { Id = Guid.NewGuid(), Sku = "BOLT", Quantity = 1, UnitPrice = 19.99m }, new LineDocument { Id = Guid.NewGuid(), Sku = "GEAR", Quantity = 1, UnitPrice = 19.99m }],
        Skus = ["BOLT", "GEAR"],
        ShipTo = new ShippingAddress { Country = "IT", City = "Rome", Floor = 2 },
    };
}
