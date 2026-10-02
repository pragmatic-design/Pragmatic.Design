using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Agent.Client;
using Pragmatic.Discovery.Abstractions;

namespace Pragmatic.Agent.Discovery;

/// <summary>
///     Wires the Agent KV as the <see cref="IDiscoveryBackend"/> storage so cross-host topology is
///     stored and gossip-replicated by the Agent fabric.
/// </summary>
public static class PragmaticBuilderAgentDiscoveryExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Replaces the registered <see cref="IDiscoveryBackend"/> (e.g. the in-memory default) with
        ///     <see cref="AgentDiscoveryBackend"/>. Call alongside <c>AddDiscovery()</c> and
        ///     <c>UseAgent()</c> — the latter registers the <see cref="AgentConnection"/> this backend uses.
        /// </summary>
        /// <example>
        /// <code>
        /// builder.UseAgent();               // registers AgentConnection
        /// builder.Services.AddDiscovery();  // registers IDiscoveryService + default backend
        /// builder.Services.UseAgentDiscovery();
        /// </code>
        /// </example>
        public IServiceCollection UseAgentDiscovery()
        {
            services.RemoveAll<IDiscoveryBackend>();
            services.AddSingleton<IDiscoveryBackend>(sp =>
                new AgentDiscoveryBackend(sp.GetRequiredService<AgentConnection>()));
            return services;
        }
    }
}
