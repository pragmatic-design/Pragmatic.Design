using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Endpoints.Mcp;

namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>
///     The suite's <c>ShowcaseWebFactory</c> on an engine host (Sql here, linked by the documents host's copy of the suite). Two differences from
///     the EF Core host's: the content root is the output, where the engine host's appsettings.json and localization/ are linked; and the
///     connection strings go through <c>UseSetting</c>, because the engine host resolves its data sources from them while it is built.
/// </summary>
public sealed class ShowcaseWebFactory(
    PostgresFixture fixture,
    IReadOnlyDictionary<string, string?>? extraConfig = null,
    Action<IServiceCollection>? extraServices = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseSetting("ConnectionStrings:App", fixture.AppConnectionString);
        builder.UseSetting("ConnectionStrings:Financial", fixture.FinancialConnectionString);

        // Scenario overrides go through UseSetting too, as on the EF Core host: configuration added in ConfigureAppConfiguration is not yet
        // visible while Program's PragmaticApp callback runs.
        if (extraConfig is not null)
        {
            foreach (var kv in extraConfig)
                builder.UseSetting(kv.Key, kv.Value);
        }

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:App"] = fixture.AppConnectionString,
                ["ConnectionStrings:Financial"] = fixture.FinancialConnectionString,
                ["DetailedErrors"] = "true",
                ["Pragmatic:MaintenanceMode:EnableOnStartupFailure"] = "false",
                ["Showcase:SeedDemoData"] = "false",
            };

            if (extraConfig is not null)
            {
                foreach (var kv in extraConfig)
                    settings[kv.Key] = kv.Value;
            }

            config.AddInMemoryCollection(settings);
        });

        // The EventLog provider fails without admin privileges on Windows.
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });

        // TestServer has no sockets: route MCP tool self-calls through its in-memory handler.
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient(McpToolExecutor.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(sp =>
                    sp.GetRequiredService<IServer>() is TestServer testServer
                        ? testServer.CreateHandler()
                        : new HttpClientHandler());

            extraServices?.Invoke(services);
        });
    }
}
