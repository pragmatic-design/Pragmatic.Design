using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;

namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     A test class gets the host its overrides call for, and no other: the suite's shared host when it
///     changes nothing, a host of its own when it changes the host's configuration or services, a host per
///     test when it changes the database.
/// </summary>
/// <remarks>
///     The probes below are test classes without tests, initialised by hand the way xUnit initialises one
///     instance per test. A class that changed the host and still got the shared one would run against the
///     configuration it asked to replace, and pass or fail for the wrong reason.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class TheHostIsBuiltOncePerWhatItNeeds(PostgresFixture database) : IAsyncLifetime
{
    private readonly List<TimeOffTestBase> _started = [];

    [Fact]
    public async Task TwoTestsOfClassesThatChangeNothing_RunOnOneHost()
    {
        var first = await StartedAsync(new ChangesNothing(database));
        var second = await StartedAsync(new AlsoChangesNothing(database));

        second.HostServices.Should().BeSameAs(first.HostServices);
    }

    [Fact]
    public async Task AClassThatReplacesAService_HasAHostOfItsOwn_SharedByItsTests()
    {
        var shared = await StartedAsync(new ChangesNothing(database));
        var first = await StartedAsync(new ReplacesAService(database));
        var second = await StartedAsync(new ReplacesAService(database));

        first.HostServices.Should().NotBeSameAs(shared.HostServices, "it replaced a service the shared host has");
        second.HostServices.Should().BeSameAs(first.HostServices, "its tests run on the host it asked for");
    }

    [Fact]
    public async Task AClassThatChangesTheSettings_HasAHostOfItsOwn()
    {
        var shared = await StartedAsync(new ChangesNothing(database));
        var changed = await StartedAsync(new ChangesTheSettings(database));

        changed.HostServices.Should().NotBeSameAs(shared.HostServices);
    }

    [Fact]
    public async Task AClassOnADatabaseOfItsOwn_HasAHostPerTest()
    {
        var first = await StartedAsync(new UsesItsOwnDatabase(database));
        var second = await StartedAsync(new UsesItsOwnDatabase(database));

        second.HostServices.Should().NotBeSameAs(first.HostServices,
            "each test asked for a new database, and a host runs on one");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var probe in _started)
            await probe.DisposeAsync();
    }

    private async Task<T> StartedAsync<T>(T probe) where T : TimeOffTestBase
    {
        await probe.InitializeAsync();
        _started.Add(probe);
        return probe;
    }

    private abstract class Probe(PostgresFixture database) : TimeOffTestBase(database)
    {
        public IServiceProvider HostServices => Services;
    }

    private sealed class ChangesNothing(PostgresFixture database) : Probe(database);

    private sealed class AlsoChangesNothing(PostgresFixture database) : Probe(database);

    private sealed class ReplacesAService(PostgresFixture database) : Probe(database)
    {
        protected override void ConfigureServices(IServiceCollection services)
            => services.AddSingleton(new Marker());
    }

    private sealed class ChangesTheSettings(PostgresFixture database) : Probe(database)
    {
        protected override IReadOnlyDictionary<string, string?> Settings =>
            new Dictionary<string, string?> { ["TimeOff:Probe"] = "changed" };
    }

    private sealed class UsesItsOwnDatabase(PostgresFixture database) : Probe(database)
    {
        protected override Task<string> ConnectionStringAsync(PostgresFixture database) =>
            database.CreateDatabaseAsync();
    }

    private sealed class Marker;
}
