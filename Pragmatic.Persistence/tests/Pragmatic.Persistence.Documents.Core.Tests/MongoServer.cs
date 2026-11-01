using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     MongoDB 8 in a container, with a root user (SCRAM-SHA-256). The image's entrypoint starts a first mongod on localhost only to create the
///     user, then the real one: ready when "Waiting for connections" was logged twice. A start that failed is <see cref="Unavailable" />, which
///     <c>The_server_started</c> reports.
/// </summary>
public sealed class MongoServer : IAsyncLifetime
{
    public const string User = "root";
    public const string Password = "spike-Pa55";
    private IContainer? _container;

    public string? Unavailable { get; private set; }

    public string ConnectionString { get; private set; } = "";

    public MongoClient Client { get; private set; } = null!;

    /// <summary>The SQL workload's seed (100 customers, 10,000 orders), and the same data as the documents written in <see cref="Workload" />.</summary>
    public Seed Seed { get; } = new();

    public List<OrderDocument> Orders { get; private set; } = [];

    public List<CustomerDocument> Customers { get; private set; } = [];

    /// <summary>The database the workload is seeded in: the chain tests read it (and write only documents of their own).</summary>
    public const string Workload = "workload";

    public DocSession Session() => new(Client, Workload);

    public async Task InitializeAsync()
    {
        try
        {
            _container = Start(new ContainerBuilder().WithImage("mongo:9"));
            await _container.StartAsync();
            ConnectionString = $"mongodb://{User}:{Password}@{_container.Hostname}:{_container.GetMappedPublicPort(27017)}/?authSource=admin";
            Client = new MongoClient(ConnectionString);
            Orders = DocumentSeed.Orders(Seed);
            Customers = DocumentSeed.Customers(Seed);
            var session = Session();
            await session.InsertManyAsync(Customers);
            await session.InsertManyAsync(Orders);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            var log = await TailAsync(_container);
            Unavailable = "MongoDB did not start: " + e.GetType().Name + ": " + e.Message.Split('\n')[0] + " — log: " + log;
        }
    }

    /// <summary>The image with the root user, its port, and the wait for the second "Waiting for connections".</summary>
    public static IContainer Start(ContainerBuilder builder)
        => builder.WithEnvironment("MONGO_INITDB_ROOT_USERNAME", User).WithEnvironment("MONGO_INITDB_ROOT_PASSWORD", Password).WithPortBinding(27017, true)
            .WithWaitStrategy(Wait.ForUnixContainer().AddCustomWaitStrategy(new LoggedUnlessExited("Waiting for connections", 2), static wait => wait.WithTimeout(TimeSpan.FromMinutes(2)))).Build();

    /// <summary>
    ///     The container's last log lines, what a failed start says about itself; null when there are none to read (no container, or Docker did
    ///     not answer).
    /// </summary>
    public static async Task<string?> TailAsync(IContainer? container)
    {
        if (container is null)
            return null;
        try
        {
            var (stdout, stderr) = await container.GetLogsAsync(timestampsEnabled: false);
            var lines = (stdout + stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" | ", lines.TakeLast(6));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null; // no logs: the container was never created, Docker did not answer
        }
    }

    public async Task DisposeAsync()
    {
        if (Client is not null)
            await Client.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>A fresh database name per test: the tests share the server, not their collections.</summary>
    public static string Database() => "t" + Guid.NewGuid().ToString("N")[..12];
}

[CollectionDefinition("mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoServer>;
