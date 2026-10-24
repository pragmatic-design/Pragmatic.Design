using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;

namespace PragmaticDocumentSamples.Orders;

/// <summary>Writes of whole documents: insert, a replace at the version the object holds, a stale copy refused, upsert, delete.</summary>
internal static class OrderWrites
{
    public static async Task<SampleRun> RunAsync(string backend, DocSession session)
    {
        var checks = new Checks(backend);
        var order = new OrderDocument
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Number = "ORD-SAMPLE",
            Status = OrderStatus.Open,
            Total = 21m,
            CreatedAt = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero),
            Lines = [new LineDocument { Id = Guid.NewGuid(), Sku = "GEAR", Quantity = 2, UnitPrice = 10.5m }],
            Skus = ["GEAR"],
            ShipTo = new ShippingAddress { Country = "IT", City = "Rome", Floor = 1 },
        };
        await session.InsertAsync(order);
        var read = (await session.Query<OrderDocument>().FirstOrDefaultAsync((o, k) => o.CustomerId == k.Customer && o.Id == k.Id, (Customer: order.CustomerId, order.Id)))!;
        read.Lines.Add(new LineDocument { Id = Guid.NewGuid(), Sku = "NUT", Quantity = 1, UnitPrice = 3m });
        read.Total = 24m;
        await session.ReplaceAsync(read);

        var stale = false;
        try
        {
            await session.ReplaceAsync(order); // read before the replace: version 0, the stored one is 1
        }
        catch (DocConcurrencyException)
        {
            stale = true;
        }

        read.Status = OrderStatus.Paid;
        await session.UpsertAsync(read);
        var stored = (await session.Query<OrderDocument>().FirstOrDefaultAsync((o, k) => o.CustomerId == k.Customer && o.Id == k.Id, (Customer: order.CustomerId, order.Id)))!;
        checks.Check("insert, replace at the version, a stale copy refused, upsert", stale && read.Version == 2 && stored is { Version: 2, Total: 24m, Status: OrderStatus.Paid } && stored.Lines.Count == 2);

        await session.DeleteAsync(stored);
        checks.Check("delete at the version", !await session.Query<OrderDocument>().AnyAsync((o, id) => o.CustomerId == id, order.CustomerId));
        return checks.Run;
    }
}
