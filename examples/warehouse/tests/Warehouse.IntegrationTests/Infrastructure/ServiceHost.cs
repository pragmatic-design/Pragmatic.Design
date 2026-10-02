using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.ControlPlane;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     One instance of one of the example's hosts, listening on a real port.
/// </summary>
/// <remarks>
///     <para>
///         A real port because the gateway forwards over its own HTTP client, which reaches sockets and
///         not a test server. Configuration is supplied and nothing else is replaced: the generated
///         wiring, the pipeline and the Agent connection run as they do in a deployment.
///     </para>
///     <para>
///         Settings go through <c>UseSetting</c> because the hosts read configuration while they register
///         their services. Not Development, so the committed development settings stay out.
///     </para>
/// </remarks>
internal sealed class ServiceHost<TEntryPoint>(IReadOnlyDictionary<string, string?> settings)
    : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    /// <summary>The settings this instance was started with, for a restart that has to be the same instance.</summary>
    public IReadOnlyDictionary<string, string?> Settings { get; } = settings;

    /// <summary>Starts the host on a free port, or on <paramref name="port" /> when a restart needs the same one.</summary>
    public ServiceHost<TEntryPoint> Start(int port = 0)
    {
        UseKestrel(port);
        StartServer();
        return this;
    }

    /// <summary>The port the host listens on.</summary>
    public int Port => new Uri(Address).Port;

    /// <summary>The address the host listens on, as a gateway backend names it.</summary>
    public string Address => Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.First();

    /// <summary>What this instance writes in <c>X-Served-By</c>: its host name and its instance id.</summary>
    public string ServedBy
    {
        get
        {
            var identity = Services.GetRequiredService<IHostIdentity>();
            return $"{identity.HostName}/{identity.HostId}";
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // A host that fails to start must fail the suite, not answer 503 from maintenance mode.
        builder.UseSetting("Pragmatic:MaintenanceMode:EnableOnStartupFailure", "false");

        foreach (var (key, value) in Settings)
            builder.UseSetting(key, value);
    }
}
