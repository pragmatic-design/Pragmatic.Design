// Pragmatic.Discovery Samples - DiscoveryHostedService auto-registration lifecycle.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Discovery.Extensions;

namespace Pragmatic.Discovery.Samples;

/// <summary>
/// Demonstrates the <c>DiscoveryHostedService</c> startup lifecycle that <c>AddDiscovery()</c> wires in
/// as an <see cref="IHostedService"/>. The service auto-registers the current host's topology from
/// SG-emitted assembly metadata when <c>AutoRegisterOnStartup</c> is true.
/// </summary>
/// <remarks>
/// This samples assembly has no <c>[assembly: PragmaticMetadata(HostTopology, ...)]</c> attribute
/// (that is emitted by the Composition SG in a real host with a [Module] class), so the hosted service
/// takes its "no metadata found" branch and logs a warning, then skips registration. In a real host the
/// topology is discovered automatically and registered with no extra code.
/// </remarks>
internal static class HostedServiceSample
{
    public static async Task RunAsync()
    {
        SampleConsole.Header("DiscoveryHostedService — Auto-Registration Lifecycle");

        var services = new ServiceCollection();
        services.AddLogging();
        // Provide IHostEnvironment so the hosted service can check IsDevelopment().
        services.AddSingleton<IHostEnvironment>(new SampleHostEnvironment());
        services.AddDiscovery(o =>
        {
            o.AutoRegisterOnStartup = true;  // hosted service will attempt to read & register topology
            o.ValidateOnStartup = true;      // and validate it after registration
            o.ThrowOnValidationFailure = false;
        });

        await using var provider = services.BuildServiceProvider();

        // AddDiscovery registered DiscoveryHostedService as an IHostedService.
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        SampleConsole.Step($"AddDiscovery registered {hostedServices.Count} IHostedService(s).");

        // Drive the startup lifecycle exactly as the .NET generic host would.
        SampleConsole.Step("Calling StartAsync() on each hosted service...");
        foreach (var hosted in hostedServices)
            await hosted.StartAsync(CancellationToken.None);

        SampleConsole.Info("StartAsync completed. With no HostTopology metadata in this samples");
        SampleConsole.Info("assembly, the service logged a warning and skipped registration —");
        SampleConsole.Info("in a real host it would auto-register the SG-emitted topology.");

        foreach (var hosted in hostedServices)
            await hosted.StopAsync(CancellationToken.None);

        SampleConsole.Blank();
    }

    /// <summary>Minimal <see cref="IHostEnvironment"/> for the standalone sample.</summary>
    private sealed class SampleHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Pragmatic.Discovery.Samples";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
