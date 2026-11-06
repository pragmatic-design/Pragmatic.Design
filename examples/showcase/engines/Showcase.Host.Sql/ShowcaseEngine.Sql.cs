using Npgsql;
using Pragmatic.Composition;
using Pragmatic.Migrations.Extensions;
using Pragmatic.Notifications.Configuration;
using Pragmatic.Persistence.Sql;
using Showcase.Accounts;
using Showcase.Billing;
using Showcase.Booking;
using Showcase.Catalog;

namespace Showcase.Host;

/// <summary>The host's wiring on the Sql engine, over PostgreSQL: in place of src/Showcase.Host/Engine/ShowcaseEngine.EfCore.cs.</summary>
internal static partial class ShowcaseEngine
{
    private const string AppDatabase = "App";
    private const string FinancialDatabase = "Financial";

    /// <summary>
    ///     Each boundary's session, on the database ShowcaseHostModule puts it in: Accounts, Booking and Catalog in ConnectionStrings:App,
    ///     Billing in ConnectionStrings:Financial. One data source per database, which the container creates and disposes.
    /// </summary>
    /// <remarks>
    ///     The connection strings are read when the data source is first resolved, not here: configuration a test host adds arrives after
    ///     this callback has run.
    /// </remarks>
    public static partial void UsePersistence(IPragmaticBuilder app)
    {
        app.Services.AddKeyedSingleton(AppDatabase, static (sp, _) => DataSource(sp, AppDatabase));
        app.Services.AddKeyedSingleton(FinancialDatabase, static (sp, _) => DataSource(sp, FinancialDatabase));

        app.Services.AddAccountsBoundaryPersistence(static (sp, filters) => Session(sp, AppDatabase, filters));
        app.Services.AddBookingBoundaryPersistence(static (sp, filters) => Session(sp, AppDatabase, filters));
        app.Services.AddCatalogBoundaryPersistence(static (sp, filters) => Session(sp, AppDatabase, filters));
        app.Services.AddBillingBoundaryPersistence(static (sp, filters) => Session(sp, FinancialDatabase, filters));
    }

    /// <summary>Pragmatic Migrations, over the schema the Sql engine's boundaries declare for each database.</summary>
    public static partial void UseSchema(IPragmaticBuilder app) => app.UsePragmaticMigrations();

    /// <summary>Nothing to add: the Booking boundary declares [StoresNotifications], and its generated registration puts the store on its session.</summary>
    public static partial void UseNotificationStore(NotificationsBuilder notifications, IPragmaticBuilder app)
    {
    }

    /// <summary>Not on this engine: the generated user resolver reads through EF Core (#549).</summary>
    public static partial void UseUserCulture(IPragmaticBuilder app)
    {
    }

    private static NpgsqlDataSource DataSource(IServiceProvider services, string database)
        => NpgsqlDataSource.Create(services.GetRequiredService<IConfiguration>().GetConnectionString(database)
            ?? throw new InvalidOperationException($"ConnectionStrings:{database} is required."));

    private static SqlSession Session(IServiceProvider services, string database, Pragmatic.Persistence.Sql.Filters.ISqlFilters filters)
        => new(services.GetRequiredKeyedService<NpgsqlDataSource>(database).OpenConnection(), SqlDialect.PostgreSql) { Filters = filters };
}
