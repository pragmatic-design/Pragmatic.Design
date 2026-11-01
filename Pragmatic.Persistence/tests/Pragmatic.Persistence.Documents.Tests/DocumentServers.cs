using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Core.Tests;
using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>The two document stores a test runs on.</summary>
public enum Store
{
    Mongo,
    Cosmos,
}

/// <summary>
///     MongoDB 8 as a one-member replica set, so a unit of work's save runs in a transaction (the documents tests' <c>MongoReplicaSet</c>: no
///     authentication, which <c>MongoServer</c> covers there), and the Cosmos DB emulator (the Linux vNext image, as <c>CosmosServer</c>), without
///     their workload: each test writes its own documents in a database of its own. A store that does not start fails the tests: they are the
///     signal that Pragmatic's entities hold on both. A standalone MongoDB's save (no transaction) runs in the documents samples.
/// </summary>
public sealed class DocumentServers : IAsyncLifetime
{
    private const string CosmosKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="; // the emulator's well-known key

    // The vNext emulator is published only under a moving preview tag: the build the documents suite pins (CosmosServer).
    private const string CosmosImage = "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview@sha256:db53613dae19d0a3d5ad50688d14aa6e957af082dafea56b52e1bd3a6b3f1406";
    private IContainer? _mongo;
    private IContainer? _cosmos;
    private MongoClient? _mongoClient;
    private CosmosClient? _cosmosClient;

    public static TheoryData<Store> Stores => [Store.Mongo, Store.Cosmos];

    public async Task InitializeAsync()
    {
        _mongo = new ContainerBuilder().WithImage("mongo:9").WithCommand("--replSet", "rs0", "--bind_ip_all").WithPortBinding(27017, true)
            .WithWaitStrategy(Wait.ForUnixContainer().AddCustomWaitStrategy(new LoggedUnlessExited("Waiting for connections"), static wait => wait.WithTimeout(TimeSpan.FromMinutes(2)))).Build();
        _cosmos = new ContainerBuilder().WithImage(CosmosImage).WithPortBinding(8081, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Now listening on", static wait => wait.WithTimeout(TimeSpan.FromMinutes(3)))).Build();
        await Task.WhenAll(_mongo.StartAsync(), _cosmos.StartAsync());
        await MongoshAsync("rs.initiate({ _id: 'rs0', members: [{ _id: 0, host: 'localhost:27017' }] })");
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while ((await MongoshAsync("db.hello().isWritablePrimary")).Trim() != "true")
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The replica set's member did not become the writable primary within 60 s.");
            await Task.Delay(500); // the election: a condition polled, not a fixed wait
        }

        _mongoClient = new MongoClient($"mongodb://{_mongo.Hostname}:{_mongo.GetMappedPublicPort(27017)}/");
        _cosmosClient = new CosmosClient($"AccountEndpoint=http://{_cosmos.Hostname}:{_cosmos.GetMappedPublicPort(8081)}/;AccountKey={CosmosKey};");
    }

    /// <summary>A session on a database of its own, for the caller <paramref name="filters" /> describes: the tests share the servers, not their collections.</summary>
    public DocSession Session(Store store, IDocFilters? filters = null)
    {
        var database = "t" + Guid.NewGuid().ToString("N")[..12];
        return store == Store.Mongo ? new DocSession(_mongoClient!, database) { Filters = filters } : new DocSession(_cosmosClient!, database) { Filters = filters };
    }

    /// <summary>Another session on the same database, for another caller.</summary>
    public static DocSession As(DocSession session, IDocFilters? filters)
        => session.Mongo is { } mongo ? new DocSession(mongo, session.Database) { Filters = filters } : new DocSession(session.Cosmos!, session.Database) { Filters = filters };

    public async Task DisposeAsync()
    {
        if (_mongoClient is not null)
            await _mongoClient.DisposeAsync();
        _cosmosClient?.Dispose();
        foreach (var container in new[] { _mongo, _cosmos })
        {
            if (container is not null)
                await container.DisposeAsync();
        }
    }

    private async Task<string> MongoshAsync(string script)
    {
        var result = await _mongo!.ExecAsync(["mongosh", "--quiet", "--eval", script]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"mongosh {script}: exit {result.ExitCode}: {result.Stderr}");
        return result.Stdout;
    }
}

[CollectionDefinition("document servers")]
public sealed class DocumentServersCollection : ICollectionFixture<DocumentServers>;
