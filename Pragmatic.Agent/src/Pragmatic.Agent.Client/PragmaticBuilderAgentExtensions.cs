using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Agent.Client.Stores;
using Pragmatic.Composition;
using Pragmatic.Configuration;
using Pragmatic.Configuration.Bridge;
using Pragmatic.ControlPlane;
using Pragmatic.FeatureFlags;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Agent.Client;

/// <summary>
///     Extension methods for configuring the Pragmatic Agent on <see cref="IPragmaticBuilder" />.
/// </summary>
public static class PragmaticBuilderAgentExtensions
{
    private const string ConfigSection = "Pragmatic:Agent";

    extension(IPragmaticBuilder builder)
    {
        /// <summary>
        ///     Connects the application to the local Pragmatic Agent daemon.
        ///     Reads configuration from <c>Pragmatic:Agent:SocketPath</c>.
        ///     If Agent is unreachable, the app continues in L0 mode (graceful degradation).
        /// </summary>
        /// <remarks>
        ///     This single call replaces all NoOp/InMemory backends with Agent-backed implementations:
        ///     <list type="bullet">
        ///         <item><c>IControlPlane</c> -> <c>AgentControlPlane</c></item>
        ///         <item><c>IConfigurationStore</c> -> <c>AgentConfigurationStore</c></item>
        ///         <item><c>IFeatureFlagStore</c> -> <c>AgentFeatureFlagStore</c></item>
        ///         <item><c>ITenantStore</c> -> <c>AgentTenantStore</c></item>
        ///     </list>
        ///     Additional Agent-backed integrations may be added in future phases, but the core store replacements
        ///     above already happen today.
        /// </remarks>
        public IPragmaticBuilder UseAgent()
        {
            var socketPath = builder.Configuration[$"{ConfigSection}:SocketPath"];

            var options = new AgentOptions();
            if (!string.IsNullOrEmpty(socketPath))
                options.SocketPath = socketPath;
            options.Announce = AnnouncementFrom(builder.Configuration);

            RegisterAgentServices(builder, options);
            return builder;
        }

        /// <summary>
        ///     Connects the application to the local Pragmatic Agent daemon with explicit configuration.
        /// </summary>
        /// <example>
        /// <code>
        /// builder.UseAgent(agent =>
        /// {
        ///     agent.SocketPath = "/var/run/pragmatic/agent.sock";
        ///     agent.HeartbeatInterval = TimeSpan.FromSeconds(10);
        /// });
        /// </code>
        /// </example>
        public IPragmaticBuilder UseAgent(Action<AgentOptions> configure)
        {
            var options = new AgentOptions();
            configure(options);

            RegisterAgentServices(builder, options);
            return builder;
        }
    }

    /// <summary>
    ///     The route configured under <c>Pragmatic:Agent:Announce</c>, or null when no <c>RouteId</c> is set.
    /// </summary>
    /// <remarks>
    ///     A route id without a path or an address is refused at startup: announcing nothing would leave
    ///     the instance out of the gateway's rotation, and nothing would say why.
    /// </remarks>
    internal static AgentRouteAnnouncement? AnnouncementFrom(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var section = configuration.GetSection($"{ConfigSection}:Announce");
        var routeId = section["RouteId"];
        if (string.IsNullOrEmpty(routeId))
            return null;

        var path = section["Path"];
        var address = section["Address"];
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(address))
            throw new InvalidOperationException(
                $"The route '{routeId}' this host announces needs both {ConfigSection}:Announce:Path (the "
                + $"pattern the gateway matches) and {ConfigSection}:Announce:Address (where the gateway reaches it).");

        return new AgentRouteAnnouncement
        {
            RouteId = routeId,
            Path = path,
            PathRemovePrefix = section["PathRemovePrefix"],
            RequireAuth = bool.TryParse(section["RequireAuth"], out var requireAuth) && requireAuth,
            Address = address,
        };
    }

    private static void RegisterAgentServices(IPragmaticBuilder builder, AgentOptions options)
    {
        var services = builder.Services;

        // Auto-start Agent daemon in Development mode
        var autoStart = new AgentAutoStart();
        autoStart.StartIfNeeded(options);
        services.AddSingleton(autoStart); // Keeps reference alive, Dispose stops the process

        services.AddSingleton(options);

        // Created here, because the configuration source below reads through it before the container
        // exists. Constructing opens nothing: the heartbeat service connects it after host start, so a
        // daemon not yet running still degrades gracefully. Registered through a factory, not as an
        // instance, so the container disposes it at shutdown — the disconnect is what retires this host's
        // ephemeral announcements.
        var connection = new AgentConnection(options.SocketPath);
        services.AddSingleton(_ => connection);

        // Settings written through the Agent (config/{Section}:{Key}) reach [Configuration] classes like
        // appsettings do, and IOptionsMonitor<T> sees a runtime change. Only when the host's
        // configuration is a builder — a host's ConfigurationManager is.
        if (builder.Configuration is IConfigurationBuilder configuration)
        {
            configuration.AddPragmaticStore(
                new AgentConfigurationStore(connection),
                EnvironmentProfile.From(builder.Environment.EnvironmentName));
        }

        // Replace IControlPlane with Agent-backed implementation. Passing the provider lets it resolve
        // IHostCommandDispatcher to execute commands the daemon pushes over the socket.
        services.RemoveAll<IControlPlane>();
        services.AddSingleton<IControlPlane>(sp => new AgentControlPlane(sp.GetRequiredService<AgentConnection>(), sp));

        // Built when the host starts, so it listens for the commands the Agent delivers from then on — a
        // singleton is otherwise built when first asked for, and only the command handlers ask.
        services.AddHostedService(sp => new AgentCommandListener(sp.GetRequiredService<IControlPlane>()));

        // Replace IConfigurationStore with Agent-backed implementation, preserving any
        // previously-registered IConfigurationStore (e.g. the in-memory store the config seeder writes
        // to) as a fallback so config reads/writes keep working when the Agent daemon is unreachable
        // (L0 mode). Captured by service contract before RemoveAll, same as the feature-flag store.
        var configFallbackDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IConfigurationStore));
        services.RemoveAll<IConfigurationStore>();
        services.AddSingleton<IConfigurationStore>(sp =>
        {
            var fallback = configFallbackDescriptor is null
                ? null
                : ResolveDescriptor(sp, configFallbackDescriptor) as IConfigurationStore;
            return new AgentConfigurationStore(
                sp.GetRequiredService<AgentConnection>(),
                fallback,
                sp.GetService<ISensitiveKeyClassifier>(),
                sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>());
        });

        // Replace IFeatureFlagStore with Agent-backed implementation, preserving any
        // previously-registered IFeatureFlagStore (e.g. the in-memory store seeded with
        // targeting rules) as a fallback so that rule evaluation still works when the Agent
        // daemon is unreachable. We capture the existing descriptor by its IFeatureFlagStore
        // service contract — not by a hard-coded implementation FullName — so a rename of the
        // concrete store can't silently break the fallback, and Agent.Client still avoids a
        // hard dependency on Pragmatic.FeatureFlags.
        var fallbackDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IFeatureFlagStore));
        services.RemoveAll<IFeatureFlagStore>();
        services.AddSingleton<IFeatureFlagStore>(sp =>
        {
            var fallback = fallbackDescriptor is null
                ? null
                : ResolveDescriptor(sp, fallbackDescriptor) as IFeatureFlagStore;
            return new AgentFeatureFlagStore(sp.GetRequiredService<AgentConnection>(), fallback);
        });

        // Replace ITenantStore with Agent-backed implementation, preserving any previously-registered
        // ITenantStore (e.g. the seeded InMemoryTenantStore) as the L0 fallback — consistent with the
        // config and feature-flag stores: Agent-backed data stores fall back uniformly.
        var tenantFallbackDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(ITenantStore));
        services.RemoveAll<ITenantStore>();
        services.AddSingleton<ITenantStore>(sp =>
        {
            var fallback = tenantFallbackDescriptor is null
                ? null
                : ResolveDescriptor(sp, tenantFallbackDescriptor) as ITenantStore;
            // ⚠️ Wrapped, like the registration this replaces. RemoveAll takes the generated host's
            // ObservedTenantStore with it, and a tenant created through the Agent would then reach no
            // ITenantLifecycleObserver — the [Lookup] caches would follow tenants everywhere except
            // the deployments that use the Agent, which is the hardest place to notice it.
            return new ObservedTenantStore(
                new AgentTenantStore(sp.GetRequiredService<AgentConnection>(), fallback),
                sp.GetServices<ITenantLifecycleObserver>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<ObservedTenantStore>>());
        });

        // Replace the NoOp (always-leader) cluster leadership with the Agent KV-lease election, so
        // cluster-wide singleton work elects one host instead of every host acting as leader.
        services.RemoveAll<IClusterLeadership>();
        services.AddSingleton<IClusterLeadership>(sp => new AgentClusterLeadership(
            sp.GetRequiredService<AgentConnection>(),
            sp.GetRequiredService<AgentOptions>(),
            sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()));

        // This instance as the Agent knows it, and its announcement: one, shared by the heartbeat (which
        // announces when it registers) and the control plane (which announces again when the host reports a
        // state — a drain leaving the rotation).
        services.AddSingleton(sp => new AgentInstanceAnnouncer(
            sp.GetRequiredService<AgentConnection>(),
            sp.GetRequiredService<AgentOptions>(),
            sp.GetService<IHostIdentity>(),
            sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()));

        // Background heartbeat service. IHostIdentity is optional — resolved when the host template
        // registered it (LocalHostIdentity), otherwise the roster falls back to Tenant/now.
        services.AddHostedService(sp => new AgentHeartbeatService(
            sp.GetRequiredService<AgentConnection>(),
            sp.GetRequiredService<AgentOptions>(),
            sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>(),
            sp.GetService<IHostIdentity>(),
            sp.GetService<IHostStatus>(),
            sp.GetRequiredService<AgentInstanceAnnouncer>()));
    }

    /// <summary>
    ///     Materializes the instance described by <paramref name="descriptor"/> using the
    ///     container. The descriptor was captured before <c>RemoveAll</c>, so it can no longer be
    ///     resolved by service type — we honor its instance / factory / implementation-type shape
    ///     directly.
    /// </summary>
    private static object? ResolveDescriptor(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is not null)
            return descriptor.ImplementationInstance;

        if (descriptor.ImplementationFactory is not null)
            return descriptor.ImplementationFactory(sp);

        if (descriptor.ImplementationType is not null)
            return ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);

        return null;
    }
}
