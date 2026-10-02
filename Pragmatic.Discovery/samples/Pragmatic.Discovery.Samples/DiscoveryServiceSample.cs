// Pragmatic.Discovery Samples - DI registration + IDiscoveryService runtime usage.

using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Extensions;
using Pragmatic.Discovery.Options;

namespace Pragmatic.Discovery.Samples;

/// <summary>
/// Demonstrates <c>AddDiscovery()</c> DI registration and the resolved
/// <see cref="IDiscoveryService"/> being used at runtime: register hosts,
/// list them, and locate which host owns a given module.
/// </summary>
internal static class DiscoveryServiceSample
{
    public static async Task RunAsync()
    {
        SampleConsole.Header("AddDiscovery() + IDiscoveryService — Runtime Usage");

        // Register Discovery in a plain ServiceCollection (the default InMemory backend is wired in).
        // AutoRegisterOnStartup is disabled here because we register topologies explicitly below;
        // the hosted-service lifecycle is shown in HostedServiceSample.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDiscovery(o =>
        {
            o.AutoRegisterOnStartup = false;
            o.ValidateOnStartup = false;
        });

        await using var provider = services.BuildServiceProvider();
        var discovery = provider.GetRequiredService<IDiscoveryService>();
        SampleConsole.Step("Resolved IDiscoveryService from the container.");

        // Register a distributed topology: a main host and a standalone billing host.
        await discovery.RegisterAsync(SampleData.MainHost());
        await discovery.RegisterAsync(SampleData.BillingHost());
        SampleConsole.Step("Registered 'Showcase.Host' and 'Showcase.Billing.Host'.");

        var hosts = await discovery.GetAllHostsAsync();
        SampleConsole.Info($"GetAllHostsAsync → {hosts.Count} host(s).");
        foreach (var h in hosts)
            SampleConsole.Info($"  {h.HostName}: [{string.Join(", ", h.Modules.Select(m => m.ModuleName))}]");

        // Cross-host discovery: which host owns BillingModule?
        var owners = await discovery.FindHostsForModuleAsync("BillingModule");
        SampleConsole.Step($"FindHostsForModuleAsync('BillingModule') → {string.Join(", ", owners.Select(h => h.HostName))}");

        var bookingOwners = await discovery.FindHostsForModuleAsync("BookingModule");
        SampleConsole.Info($"FindHostsForModuleAsync('BookingModule') → {string.Join(", ", bookingOwners.Select(h => h.HostName))}");

        var none = await discovery.FindHostsForModuleAsync("UnknownModule");
        SampleConsole.Info($"FindHostsForModuleAsync('UnknownModule') → {(none.Count == 0 ? "(no hosts)" : string.Join(", ", none.Select(h => h.HostName)))}");

        // Section name for appsettings binding (see DiscoveryOptionsSample).
        SampleConsole.Info($"appsettings section: \"{DiscoveryOptions.SectionName}\"");

        SampleConsole.Blank();
    }
}
