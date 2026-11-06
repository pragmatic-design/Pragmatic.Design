using Pragmatic.Composition;
using Pragmatic.Migrations.Extensions;
using Pragmatic.Notifications.Configuration;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Mongo;
using Showcase.Accounts;
using Showcase.Billing;
using Showcase.Booking;
using Showcase.Catalog;

namespace Showcase.Host;

/// <summary>The host's wiring on the documents engine, over MongoDB: in place of src/Showcase.Host/Engine/ShowcaseEngine.EfCore.cs.</summary>
internal static partial class ShowcaseEngine
{
    private const string AppDatabase = "App";
    private const string FinancialDatabase = "Financial";

    /// <summary>
    ///     Each boundary's session, on the database ShowcaseHostModule puts it in: Accounts, Booking and Catalog in ConnectionStrings:App,
    ///     Billing in ConnectionStrings:Financial. Each connection string names its database in its path (<c>mongodb://host/showcase_app</c>);
    ///     one client per database, which the container creates and disposes.
    /// </summary>
    /// <remarks>
    ///     The connection strings are read when the client is first resolved, not here: configuration a test host adds arrives after this
    ///     callback has run.
    /// </remarks>
    public static partial void UsePersistence(IPragmaticBuilder app)
    {
        app.Services.AddKeyedSingleton(AppDatabase, static (sp, _) => Client(sp, AppDatabase));
        app.Services.AddKeyedSingleton(FinancialDatabase, static (sp, _) => Client(sp, FinancialDatabase));

        app.Services.AddAccountsBoundaryPersistence(static (sp, filters) => Session(sp, AppDatabase, filters));
        app.Services.AddBookingBoundaryPersistence(static (sp, filters) => Session(sp, AppDatabase, filters));
        app.Services.AddCatalogBoundaryPersistence(static (sp, filters) => Session(sp, AppDatabase, filters));
        app.Services.AddBillingBoundaryPersistence(static (sp, filters) => Session(sp, FinancialDatabase, filters));
    }

    /// <summary>
    ///     Pragmatic Migrations, with no SQL database to migrate: the generated start applies each boundary's documents schema (its
    ///     collections and indexes) with the migrations (#555).
    /// </summary>
    public static partial void UseSchema(IPragmaticBuilder app) => app.UsePragmaticMigrations();

    /// <summary>Nothing to add: the Booking boundary declares [StoresNotifications], and its generated registration puts the store on its session.</summary>
    public static partial void UseNotificationStore(NotificationsBuilder notifications, IPragmaticBuilder app)
    {
    }

    /// <summary>Not on this engine: the generated user resolver reads through EF Core (#549).</summary>
    public static partial void UseUserCulture(IPragmaticBuilder app)
    {
    }

    private static MongoClient Client(IServiceProvider services, string database)
        => new(MongoSettings.Parse(ConnectionString(services, database)));

    private static DocSession Session(IServiceProvider services, string database, Pragmatic.Persistence.Documents.Filters.IDocFilters filters)
        => new(services.GetRequiredKeyedService<MongoClient>(database), MongoSettings.Parse(ConnectionString(services, database)).Database
            ?? throw new InvalidOperationException($"ConnectionStrings:{database} names no database in its path.")) { Filters = filters };

    private static string ConnectionString(IServiceProvider services, string database)
        => services.GetRequiredService<IConfiguration>().GetConnectionString(database)
            ?? throw new InvalidOperationException($"ConnectionStrings:{database} is required.");
}
