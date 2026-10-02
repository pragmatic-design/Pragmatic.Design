using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace Showcase.Host.Distributed.Tests;

/// <summary>
///     Boots <c>Showcase.Host.Distributed</c> — the topology where Billing lives on another host.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Why this exists at all.</b> A host nothing starts can carry a real defect invisibly,
///         and every change to what it registers or serves would be unverifiable.
///     </para>
///     <para>
///         <b>The database is not migrated here.</b> This host has no schema constant and no reference
///         to <c>Pragmatic.Migrations</c>: its <c>Program.cs</c> calls <c>UseDatabaseEnsureCreated()</c>
///         in Development, so an empty database and that environment are the whole of the setup. A
///         fixture that applied migrations would be asserting something this host does not do.
///     </para>
///     <para>
///         ⚠️ <b>Maintenance mode is switched off on purpose.</b> The generated entry point runs database
///         initialisation inside a <c>try</c> that, on failure, serves 503 instead of crashing — so a
///         host that did not start would still answer, and a test that only looked for a response would
///         be green on a broken host. Container validation happens earlier, at
///         <c>builder.Build()</c>, and propagates either way; this setting makes the rest fail loudly
///         too.
///     </para>
/// </remarks>
public sealed class DistributedHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("showcase_distributed")
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

        var connectionString = _container.GetConnectionString();

        _host = new DistributedWebFactory(connectionString);
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync().ConfigureAwait(false);

        await _container.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class DistributedWebFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Development is what turns on UseDatabaseEnsureCreated() and the development identity.
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:App"] = connectionString,
                    ["DetailedErrors"] = "true",

                    // The address the Billing host would be at. Never called by these tests — what is
                    // under test is the composition, not the hop — but the remote boundary's typed
                    // HttpClient is configured from it at startup.
                    ["Pragmatic:RemoteBoundaries:Showcase.Billing:BaseUrl"] = "http://billing.invalid",

                    // See the class remark: a startup failure must crash, not serve 503.
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
