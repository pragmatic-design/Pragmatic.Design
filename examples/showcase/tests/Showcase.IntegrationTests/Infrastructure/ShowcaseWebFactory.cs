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
///     WebApplicationFactory that boots the real Showcase.Host against Testcontainers PostgreSQL.
///     Overrides only the connection strings — everything else (SG-generated DI, endpoints,
///     middleware) runs exactly as in production.
/// </summary>
public sealed class ShowcaseWebFactory(
    PostgresFixture fixture,
    IReadOnlyDictionary<string, string?>? extraConfig = null,
    Action<IServiceCollection>? extraServices = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Scenario overrides must ALSO go through UseSetting: values added via
        // ConfigureAppConfiguration land in the final configuration but are NOT yet visible
        // while Program's PragmaticApp callback runs (eager reads like the messaging transport
        // selection). UseSetting flows into the bootstrap host configuration, which
        // WebApplication.CreateBuilder chains into app configuration up front — the same
        // mechanism that makes UseEnvironment visible eagerly.
        if (extraConfig is not null)
        {
            foreach (var kv in extraConfig)
                builder.UseSetting(kv.Key, kv.Value);
        }

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Override connection strings to point at the shared Testcontainer
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:App"] = fixture.AppConnectionString,
                ["ConnectionStrings:Financial"] = fixture.FinancialConnectionString,
                ["DetailedErrors"] = "true",
                // Disable MaintenanceMode — let exceptions propagate to WebApplicationFactory
                ["Pragmatic:MaintenanceMode:EnableOnStartupFailure"] = "false"
            };

            // Scenario overrides (e.g. the Azure Service Bus transport tests)
            if (extraConfig is not null)
            {
                foreach (var kv in extraConfig)
                    settings[kv.Key] = kv.Value;
            }

            config.AddInMemoryCollection(settings);
        });

        // Suppress EventLog provider that fails without admin privileges on Windows
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

            // Scenario services (e.g. an ICommentPolicy registered the way a consumer would),
            // added last so a test can also replace something the host registered.
            extraServices?.Invoke(services);
        });
    }
}
