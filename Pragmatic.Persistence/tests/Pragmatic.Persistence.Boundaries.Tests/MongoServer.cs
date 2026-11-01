using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Pragmatic.Persistence.Documents.Core.Tests;
using Pragmatic.Persistence.Documents.Mongo;
using Xunit;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     MongoDB 8, standalone: the reviews boundary's store (a save there is not atomic, as the documents engine declares). A server that does
///     not start fails the test: the gate refuses to run this suite without Docker, so no failure here is Docker's absence.
/// </summary>
public sealed class MongoServer : IAsyncLifetime
{
    private IContainer? _mongo;

    public MongoClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _mongo = new ContainerBuilder().WithImage("mongo:9").WithPortBinding(27017, true)
            .WithWaitStrategy(Wait.ForUnixContainer().AddCustomWaitStrategy(new LoggedUnlessExited("Waiting for connections"), static wait => wait.WithTimeout(TimeSpan.FromMinutes(2)))).Build();
        await _mongo.StartAsync();
        Client = new MongoClient($"mongodb://{_mongo.Hostname}:{_mongo.GetMappedPublicPort(27017)}/");
    }

    public async Task DisposeAsync()
    {
        if (Client is not null)
            await Client.DisposeAsync();
        if (_mongo is not null)
            await _mongo.DisposeAsync();
    }
}
