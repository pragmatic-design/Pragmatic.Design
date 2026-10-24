using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     The Cosmos DB emulator (the Linux vNext image, HTTP on 8081, the emulator's well-known key) for the REST client. A start that failed is
///     reported by <c>The_emulator_started</c>, with the container's last log lines.
/// </summary>
public sealed class CosmosServer : IAsyncLifetime
{
    // The emulator's well-known key (public, the same for every emulator).
    public const string Key = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
    // The vNext emulator is published only under a moving preview tag: pinned to the build the suite was last run on (2026-10-19).
    private const string Image = "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview@sha256:db53613dae19d0a3d5ad50688d14aa6e957af082dafea56b52e1bd3a6b3f1406";
    private IContainer? _container;

    public string? Unavailable { get; private set; }

    public string ConnectionString { get; private set; } = "";

    public CosmosClient Client { get; private set; } = null!;

    /// <summary>The SQL workload's seed, and the same data as the documents written in <see cref="Workload" />.</summary>
    public Seed Seed { get; } = new();

    public List<OrderDocument> Orders { get; private set; } = [];

    public List<CustomerDocument> Customers { get; private set; } = [];

    public const string Workload = "workload";

    public DocSession Session() => new(Client, Workload);

    public async Task InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder().WithImage(Image).WithPortBinding(8081, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Now listening on", static wait => wait.WithTimeout(TimeSpan.FromMinutes(3)))).Build();
            await _container.StartAsync();
            ConnectionString = $"AccountEndpoint=http://{_container.Hostname}:{_container.GetMappedPublicPort(8081)}/;AccountKey={Key};";
            Client = new CosmosClient(ConnectionString);
            Orders = DocumentSeed.Orders(Seed);
            Customers = DocumentSeed.Customers(Seed);
            var session = Session();
            // The orders partitioned by customer; the keyset ordering (CreatedAt, id) needs its composite index on Cosmos DB.
            await session.EnsureContainerAsync<OrderDocument>([[("/CreatedAt", false), ("/id", false)]]);
            await session.EnsureContainerAsync<CustomerDocument>();
            await session.InsertManyAsync(Customers);
            await session.InsertManyAsync(Orders);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            var log = await MongoServer.TailAsync(_container);
            Unavailable = "The Cosmos emulator did not start: " + e.GetType().Name + ": " + e.Message.Split('\n')[0] + " — log: " + log;
        }
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition("cosmos")]
public sealed class CosmosCollection : ICollectionFixture<CosmosServer>;
