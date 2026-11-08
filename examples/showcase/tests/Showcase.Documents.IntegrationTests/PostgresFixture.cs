using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Pragmatic.Persistence.Documents.Core.Tests;

namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>
///     The suite's shared database fixture on the Showcase host on the documents engine: one standalone MongoDB server with the host's two
///     databases, showcase_app (Accounts, Booking, Catalog) and showcase_financial (Billing), as ShowcaseHostModule declares them.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Named <c>PostgresFixture</c> although it starts MongoDB: every test of the suite receives the fixture under that name, and the
///         sources are linked, not copied. Its two connection strings name their database in the path, as the engine host reads them.
///     </para>
///     <para>
///         Nothing is migrated or seeded here. The host applies each boundary's documents schema (its collections and indexes) at start
///         (#555), and the one test that needs seeded rows, the lookup's, reads them through EF Core and is left out.
///     </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private IContainer? _mongo;

    /// <summary>The App database (Catalog + Booking + Accounts boundaries).</summary>
    public string AppConnectionString { get; private set; } = null!;

    /// <summary>The Financial database (Billing boundary).</summary>
    public string FinancialConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _mongo = new ContainerBuilder("mongo:9").WithPortBinding(27017, true)
            .WithWaitStrategy(Wait.ForUnixContainer().AddCustomWaitStrategy(new LoggedUnlessExited("Waiting for connections"),
                static wait => wait.WithTimeout(TimeSpan.FromMinutes(2))))
            .Build();
        await _mongo.StartAsync().ConfigureAwait(false);

        var server = $"mongodb://{_mongo.Hostname}:{_mongo.GetMappedPublicPort(27017)}/";
        AppConnectionString = server + "showcase_app";
        FinancialConnectionString = server + "showcase_financial";
    }

    /// <summary>The host every test shares, built once for the collection (as on the EF Core host).</summary>
    public ShowcaseWebFactory SharedHost => _sharedHost ??= new ShowcaseWebFactory(this);

    private ShowcaseWebFactory? _sharedHost;

    public async Task DisposeAsync()
    {
        if (_sharedHost is not null)
            await _sharedHost.DisposeAsync().ConfigureAwait(false);
        if (_mongo is not null)
            await _mongo.DisposeAsync().ConfigureAwait(false);
    }
}
