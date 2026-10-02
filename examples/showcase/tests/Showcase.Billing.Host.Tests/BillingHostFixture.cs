using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace Showcase.Billing.Host.Tests;

/// <summary>
///     Boots <c>Showcase.Billing.Host</c> — the other half of the distributed demo, and the shape
///     <c>IsStandaloneHost()</c> recognises: it includes one module and references others.
/// </summary>
/// <remarks>
///     <para>
///         The same signal as the caller side of the topology, for the side that owns the module:
///         container validation is on, and an example nobody runs is an example nobody has seen work.
///     </para>
///     <para>
///         ⚠️ Maintenance mode is switched off on purpose: the generated entry point runs database
///         initialisation inside a <c>try</c> that serves 503 rather than crashing, so a host that did
///         not start would still answer and a test looking only for a response would be green on it.
///     </para>
/// </remarks>
public sealed class BillingHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("showcase_billing_standalone")
        .WithUsername("pragmatic")
        .WithPassword("Pragmatic@Test!")
        .Build();

    private WebApplicationFactory<Program>? _host;

    /// <summary>The running host. Building it is what proves its container resolves.</summary>
    public WebApplicationFactory<Program> Host =>
        _host ?? throw new InvalidOperationException("The fixture has not been initialised.");

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _host = new BillingWebFactory(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync().ConfigureAwait(false);

        await _container.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class BillingWebFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Development is what turns on UseDatabaseEnsureCreated() and the development identity.
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    // The key BillingFinancialDatabase declares.
                    ["ConnectionStrings:Financial"] = connectionString,
                    ["DetailedErrors"] = "true",

                    // Where Booking would be. Never called by these tests — what is under test is the
                    // composition, not the hop — but the remote boundary's typed HttpClient is
                    // configured from it at startup.
                    ["Pragmatic:RemoteBoundaries:Showcase.Booking:BaseUrl"] = "http://booking.invalid",

                    ["Pragmatic:MaintenanceMode:EnableOnStartupFailure"] = "false"
                }));

            // The EventLog provider fails without admin privileges on Windows.
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            });
        }
    }
}
