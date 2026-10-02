using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Logging.Configuration;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Context.Providers;

namespace Pragmatic.Logging.Extensions;

/// <summary>
/// Turns what an application declares on <see cref="ContextConfiguration" /> into registrations.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The configuration is applied to the <b>ambient</b> manager, <see cref="ContextManager.Instance" />,
///         and that same instance is what the container answers with. A manager of its own would be resolvable
///         and read by nobody: a log provider is constructed with a name and a configuration, never from the
///         container, so it enriches an entry from the ambient one — and the ASP.NET integration registers the
///         HTTP providers through a hosted service that reaches a manager at run time. Two managers meant each
///         carried half the context.
///     </para>
///     <para>
///         The providers the configuration turns off are removed rather than never added, because the ambient
///         manager exists before this runs — it registers Machine, Process and Thread in its constructor.
///     </para>
/// </remarks>
internal static class ContextEnrichmentRegistration
{
    public static void Apply(IServiceCollection services, ContextConfiguration configuration)
    {
        foreach (var provider in configuration.Providers)
            provider.Register(services);

        // The two flags that also describe what a log provider writes travel through the options the
        // OptionsAdapter reads: a switch stated once has to reach both ends.
        services.Configure<PragmaticLoggingOptions>(options =>
        {
            options.Context.IncludeCorrelationId = configuration.EnableCorrelationId;
            options.Context.IncludeUserContext = configuration.IncludeUserContext;
            options.Context.IncludeRequestContext = configuration.IncludeRequestContext;
            options.Context.IncludeMachineContext = configuration.IncludeMachineContext;
        });

        // Replace, not add: AddPragmaticLogging registers a default ContextManager before the builder
        // runs, and two registrations of it would leave which one an application gets to descriptor order.
        // ⚠️ What the container owns is a handle (AmbientContextManager), not the manager: disposing a
        // ServiceProvider would otherwise dispose the process's context manager.
        services.Replace(ServiceDescriptor.Singleton<IContextManager>(serviceProvider =>
        {
            Configure(configuration, serviceProvider);
            return new AmbientContextManager();
        }));
    }

    /// <summary>Leaves the ambient manager carrying what the configuration asks for, and nothing it turned off.</summary>
    private static void Configure(ContextConfiguration configuration, IServiceProvider services)
    {
        var manager = ContextManager.Instance;

        Keep<MachineContextProvider>(manager, "Machine", configuration.EnableEnrichment && configuration.IncludeMachineContext);
        Keep<ProcessContextProvider>(manager, "Process", configuration.EnableEnrichment && configuration.IncludeProcessContext);
        Keep<ThreadContextProvider>(manager, "Thread", configuration.EnableEnrichment && configuration.IncludeThreadContext);

        if (!configuration.EnableEnrichment)
        {
            // Enrichment off means no provider at all, the declared ones included — including any the
            // ASP.NET integration put there.
            foreach (var provider in manager.GetProviders())
                manager.UnregisterProvider(provider.Name);

            return;
        }

        foreach (var provider in configuration.Providers)
            manager.RegisterProvider(provider.Resolve(services));
    }

    /// <summary>Registers the system provider when it is wanted, removes it when it is not.</summary>
    private static void Keep<TProvider>(ContextManager manager, string name, bool wanted)
        where TProvider : IContextProvider, new()
    {
        if (wanted)
            manager.RegisterProvider(new TProvider());
        else
            manager.UnregisterProvider(name);
    }
}
