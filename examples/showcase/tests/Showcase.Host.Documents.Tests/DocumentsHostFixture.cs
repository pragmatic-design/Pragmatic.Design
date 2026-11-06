using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Pragmatic.Persistence.Documents.Core.Tests;
using Pragmatic.Persistence.Documents.Mongo;
using Xunit;

namespace Showcase.Host.Documents.Tests;

/// <summary>
///     Starts the Showcase host on the documents engine over one standalone MongoDB server with the host's two databases: showcase_app
///     (Accounts, Booking, Catalog) and showcase_financial (Billing), as ShowcaseHostModule declares them.
/// </summary>
/// <remarks>
///     ⚠️ Maintenance mode is off: the generated entry point applies the documents schema inside a <c>try</c> that serves 503 rather than
///     crashing, so a host whose schema failed would still answer, and a test looking only for a response would be green on it.
/// </remarks>
public sealed class DocumentsHostFixture : IAsyncLifetime
{
    private IContainer? _mongo;
    private WebApplicationFactory<Program>? _host;

    /// <summary>The running host.</summary>
    public WebApplicationFactory<Program> Host => _host ?? throw new InvalidOperationException("The fixture has not been initialised.");

    /// <summary>A client of the server's own, to read what the host wrote without going through it.</summary>
    public MongoClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _mongo = new ContainerBuilder("mongo:9").WithPortBinding(27017, true)
            .WithWaitStrategy(Wait.ForUnixContainer().AddCustomWaitStrategy(new LoggedUnlessExited("Waiting for connections"),
                static wait => wait.WithTimeout(TimeSpan.FromMinutes(2))))
            .Build();
        await _mongo.StartAsync().ConfigureAwait(false);

        var server = $"mongodb://{_mongo.Hostname}:{_mongo.GetMappedPublicPort(27017)}/";
        Client = new MongoClient(server);
        _host = new DocumentsHostFactory(server + "showcase_app", server + "showcase_financial");
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync().ConfigureAwait(false);
        if (Client is not null)
            await Client.DisposeAsync().ConfigureAwait(false);
        if (_mongo is not null)
            await _mongo.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class DocumentsHostFactory(string app, string financial) : WebApplicationFactory<Program>
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
