using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;
using PragmaticDocumentSamples.Flows;
using PragmaticDocumentSamples.Orders;

namespace PragmaticDocumentSamples;

/// <summary>
///     Every sample on one backend, in a fresh database the workload is written into first. The program runs them from the environment; the
///     samples' suite runs them on the servers it starts.
/// </summary>
public static class DocumentSamples
{
    /// <summary>MongoDB, a standalone server: a save there runs without a transaction (the transactions are the tests', on a replica set).</summary>
    public static async Task<SampleRun> RunMongoAsync(MongoClient client)
    {
        var (seed, orders, customers) = Workload();
        var database = Database();
        var session = new DocSession(client, database);
        await session.InsertManyAsync(customers);
        await session.InsertManyAsync(orders);
        var run = await OrderSamples.RunAllAsync("MongoDB", session, seed, orders, customers);
        run += await Pipelines.RunAsync("MongoDB", session, orders);
        run += await Joins.RunAsync("MongoDB", session, seed, orders, customers);
        run += await EntitySamples.RunAsync("MongoDB", session);
        await client.RunCommandAsync(database, w => w.Write("dropDatabase", 1));
        return run;
    }

    /// <summary>Cosmos DB, the emulator or an account: the orders partitioned by customer, the keyset ordering's composite index declared.</summary>
    public static async Task<SampleRun> RunCosmosAsync(CosmosClient client)
    {
        var (seed, orders, customers) = Workload();
        var session = new DocSession(client, Database());
        await session.EnsureContainerAsync<OrderDocument>([[("/CreatedAt", false), ("/id", false)]]);
        await session.EnsureContainerAsync<CustomerDocument>();
        await session.InsertManyAsync(customers);
        await session.InsertManyAsync(orders);
        var run = await OrderSamples.RunAllAsync("Cosmos", session, seed, orders, customers);
        run += await OrderSamples.CosmosLimitsAsync(session);
        run += await Joins.RunAsync("Cosmos", session, seed, orders, customers);
        run += await EntitySamples.RunAsync("Cosmos", session);
        return run;
    }

    private static (Seed Seed, List<OrderDocument> Orders, List<CustomerDocument> Customers) Workload()
    {
        var seed = new Seed();
        return (seed, DocumentSeed.Orders(seed), DocumentSeed.Customers(seed));
    }

    private static string Database() => "samples" + Guid.NewGuid().ToString("N")[..8];
}
