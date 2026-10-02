// Pragmatic.Discovery Samples - Custom IDiscoveryBackend implementation and registration.

using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Extensions;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Samples;

/// <summary>
/// Demonstrates implementing a custom <see cref="IDiscoveryBackend"/> and plugging it in via
/// <c>AddDiscovery().UseDiscoveryBackend&lt;T&gt;()</c>. A real implementation would talk to
/// Redis, Consul, or an HTTP registry; here we wrap a dictionary and log access.
/// </summary>
internal static class CustomBackendSample
{
    public static async Task RunAsync()
    {
        SampleConsole.Header("Custom IDiscoveryBackend — Implement & Register");

        var services = new ServiceCollection();
        services.AddLogging();
        // AddDiscovery first (wires the default InMemory backend)...
        services.AddDiscovery(o => { o.AutoRegisterOnStartup = false; o.ValidateOnStartup = false; });
        // ...then replace the backend with our own.
        services.UseDiscoveryBackend<LoggingDiscoveryBackend>();
        SampleConsole.Step("AddDiscovery().UseDiscoveryBackend<LoggingDiscoveryBackend>()");

        await using var provider = services.BuildServiceProvider();

        var backend = provider.GetRequiredService<IDiscoveryBackend>();
        SampleConsole.Info($"Resolved backend type: {backend.GetType().Name}");

        var discovery = provider.GetRequiredService<IDiscoveryService>();
        await discovery.RegisterAsync(SampleData.BillingHost());
        var all = await discovery.GetAllHostsAsync();
        SampleConsole.Info($"Round-trip through custom backend → {all.Count} host(s).");

        // Guard: UseDiscoveryBackend before AddDiscovery throws (misconfiguration is surfaced).
        try
        {
            new ServiceCollection().UseDiscoveryBackend<LoggingDiscoveryBackend>();
        }
        catch (InvalidOperationException)
        {
            SampleConsole.Step("UseDiscoveryBackend without AddDiscovery → InvalidOperationException (as designed).");
        }

        SampleConsole.Blank();
    }

    /// <summary>
    /// A minimal custom backend. Demonstrates the three-method contract; a production backend
    /// would persist to an external store instead of an in-process dictionary.
    /// </summary>
    private sealed class LoggingDiscoveryBackend : IDiscoveryBackend
    {
        private readonly Dictionary<string, HostTopologyInfo> _store = new(StringComparer.OrdinalIgnoreCase);

        public Task StoreAsync(HostTopologyInfo topology, CancellationToken ct = default)
        {
            _store[topology.HostName] = topology; // upsert semantics, per the contract
            return Task.CompletedTask;
        }

        public Task<HostTopologyInfo?> GetByHostNameAsync(string hostName, CancellationToken ct = default)
            => Task.FromResult(_store.GetValueOrDefault(hostName));

        public Task<IReadOnlyList<HostTopologyInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HostTopologyInfo>>([.. _store.Values]);
    }
}
