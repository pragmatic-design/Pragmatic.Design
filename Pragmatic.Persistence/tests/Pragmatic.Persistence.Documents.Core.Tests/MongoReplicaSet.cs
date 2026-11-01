using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Pragmatic.Persistence.Documents.Mongo;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     MongoDB 9 as a one-member replica set (transactions need one; a standalone refuses them), without authentication (a replica set with
///     users wants a key file): started with <c>--replSet rs0</c>, initiated with <c>rs.initiate</c>, ready once the member is the writable
///     primary. A start that failed is <see cref="Unavailable" />.
/// </summary>
public sealed class MongoReplicaSet : IAsyncLifetime
{
    private IContainer? _container;

    public string? Unavailable { get; private set; }

    public MongoClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder("mongo:9").WithCommand("--replSet", "rs0", "--bind_ip_all").WithPortBinding(27017, true)
                .WithWaitStrategy(Wait.ForUnixContainer().AddCustomWaitStrategy(new LoggedUnlessExited("Waiting for connections"))).Build();
            await _container.StartAsync();
            await MongoshAsync("rs.initiate({ _id: 'rs0', members: [{ _id: 0, host: 'localhost:27017' }] })");
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while ((await MongoshAsync("db.hello().isWritablePrimary")).Trim() != "true")
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("The replica set's member did not become the writable primary within 60 s.");
                await Task.Delay(500); // the election: a condition polled, not a fixed wait
            }

            Client = new MongoClient($"mongodb://{_container.Hostname}:{_container.GetMappedPublicPort(27017)}/");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            var log = await MongoServer.TailAsync(_container);
            Unavailable = "The MongoDB replica set did not start: " + e.GetType().Name + ": " + e.Message.Split('\n')[0] + " — log: " + log;
        }
    }

    private async Task<string> MongoshAsync(string script)
    {
        var result = await _container!.ExecAsync(["mongosh", "--quiet", "--eval", script]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"mongosh {script}: exit {result.ExitCode}: {result.Stderr}");
        return result.Stdout;
    }

    public async Task DisposeAsync()
    {
        if (Client is not null)
            await Client.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition("mongo-rs")]
public sealed class MongoReplicaSetCollection : ICollectionFixture<MongoReplicaSet>;
