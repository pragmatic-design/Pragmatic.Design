using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Conformance.Tests.Infrastructure;

/// <summary>
///     Starts the real <c>Conformance.Host</c> against a container.
/// </summary>
/// <remarks>
///     It overrides only the connection string: everything else — generated DI, endpoints, pipeline — runs as
///     it really would. A case that passed on a rigged host would prove nothing.
/// </remarks>
/// <param name="connectionString">The database the host runs against.</param>
/// <param name="applicationServices">
///     What an application would register on top, for the case that measures what the host does with it.
/// </param>
public sealed class ConformanceWebFactory(
    string connectionString,
    Action<IServiceCollection>? applicationServices = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Conformance"] = connectionString,
                ["DetailedErrors"] = "true",
            }));

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });

        if (applicationServices is not null)
            builder.ConfigureTestServices(applicationServices);
    }
}
