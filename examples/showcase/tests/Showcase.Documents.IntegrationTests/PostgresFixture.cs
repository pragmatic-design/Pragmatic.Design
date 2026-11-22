using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Core.Tests;
using Pragmatic.Persistence.Documents.Mongo;
using Showcase.Catalog.Entities;

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
///         Nothing is migrated here: the host applies each boundary's documents schema (its collections and indexes) at start (#555). The
///         <c>[Lookup]</c> reference rows are seeded, as the EF Core suite's fixture seeds them, before any host boots (#881).
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

        await SeedLookupCategoriesAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Writes the <c>[Lookup]</c> rows the Catalog declares, once, into the App database: reference data, present when the application
    ///     starts and read once by the preload, as <c>SeededCategories</c> says. Through the engine's own session, so each document is the one
    ///     the host reads back.
    /// </summary>
    private async Task SeedLookupCategoriesAsync()
    {
        var client = new MongoClient(AppConnectionString);
        await using (client.ConfigureAwait(false))
        {
            var session = new DocSession(client, MongoSettings.Parse(AppConnectionString).Database!);
            await session.InsertManyAsync(
            [
                Category(SeededCategories.ResortId, SeededCategories.ResortName, 0, "/resort", parent: null),
                Category(SeededCategories.BeachResortId, SeededCategories.BeachResortName, 1, "/resort/beach-resort", SeededCategories.ResortId),
            ]).ConfigureAwait(false);
        }
    }

    private static Category Category(Guid id, string name, int level, string path, Guid? parent)
    {
        var category = Showcase.Catalog.Entities.Category.Create();
        category.PersistenceId = id;
        category.SetName(name);
        category.SetDescription("Seeded lookup reference data");
        category.SetLevel(level);
        category.SetPath(path);
        category.SetParentId(parent);
        return category;
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
