using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Showcase.Host.Sql.Tests;

/// <summary>
///     Starts the Showcase host on the Sql engine over one PostgreSQL server with the host's two databases: showcase_app (Accounts, Booking,
///     Catalog) and showcase_financial (Billing), as ShowcaseHostModule declares them.
/// </summary>
/// <remarks>
///     ⚠️ Maintenance mode is off: the generated entry point runs the schema's migration inside a <c>try</c> that serves 503 rather than
///     crashing, so a host whose migration failed would still answer, and a test looking only for a response would be green on it.
/// </remarks>
public sealed class SqlHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("showcase_app")
        .WithUsername("pragmatic")
        .WithPassword("Pragmatic@Test!")
        .Build();

    private WebApplicationFactory<Program>? _host;

    /// <summary>The running host.</summary>
    public WebApplicationFactory<Program> Host => _host ?? throw new InvalidOperationException("The fixture has not been initialised.");

    /// <summary>The connection string of showcase_app, to read what the host wrote there without going through it.</summary>
    public string AppConnectionString { get; private set; } = "";

    /// <summary>The connection string of showcase_financial.</summary>
    public string FinancialConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        AppConnectionString = _container.GetConnectionString();
        FinancialConnectionString = new NpgsqlConnectionStringBuilder(AppConnectionString) { Database = "showcase_financial" }.ConnectionString;

        await using (var connection = new NpgsqlConnection(AppConnectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using var create = new NpgsqlCommand("CREATE DATABASE showcase_financial", connection);
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        _host = new SqlHostFactory(AppConnectionString, FinancialConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync().ConfigureAwait(false);

        await _container.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class SqlHostFactory(string app, string financial) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            // The host's appsettings.json and localization/ are src/Showcase.Host's, linked and copied to the output: the project folder the
            // factory would pick by default has neither.
            builder.UseContentRoot(AppContext.BaseDirectory);

            // Through UseSetting and not ConfigureAppConfiguration: the host checks its required sections while it is built, before
            // configuration added there is visible.
            builder.UseSetting("ConnectionStrings:App", app);
            builder.UseSetting("ConnectionStrings:Financial", financial);
            builder.UseSetting("DetailedErrors", "true");
            builder.UseSetting("Pragmatic:MaintenanceMode:EnableOnStartupFailure", "false");
            builder.UseSetting("Showcase:SeedDemoData", "false");

            // The EventLog provider fails without admin privileges on Windows.
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            });
        }
    }
}
