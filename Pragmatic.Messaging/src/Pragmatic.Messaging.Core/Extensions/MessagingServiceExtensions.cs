using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Serialization;

namespace Pragmatic.Messaging.Extensions;

/// <summary>
///     DI registration extensions for Pragmatic.Messaging.
/// </summary>
public static class MessagingServiceExtensions
{
    /// <summary>
    ///     Adds messaging services to the service collection.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL2026", Justification = "JsonMessageSerializer is a fallback; AOT uses SG-generated TypeRegistry.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JsonMessageSerializer is a fallback; AOT uses SG-generated TypeRegistry.")]
    public static IServiceCollection AddPragmaticMessaging(
        this IServiceCollection services,
        Action<MessagingBuilder>? configure = null)
    {
        var builder = new MessagingBuilder(services);
        configure?.Invoke(builder);

        // Configure options.
        //
        // ⚠️ Every outbox option the builder holds has to be copied here, retention included: this is
        // the second hop of a two-hop channel — EnableOutbox(o => …) writes onto builder.Options, and
        // only what this block copies reaches the MessagingOptions that OutboxDeliveryService and
        // OutboxPurgeService resolve. An option left out here is dropped: an application that asks for
        // a twelve-hour retention keeps rows for the default three days, with nothing anywhere saying
        // its setting was dropped.
        //
        // ⚠️ A property added to OutboxOptions and not added here is dropped that way, and the
        // compiler cannot see it — three places have to agree on the list. What notices is
        // TheOutboxRetentionAnApplicationConfiguresTests.EveryOptionOfTheOutbox_ReachesTheOptionsTheServicesRead,
        // which asks the type rather than a list and fails naming the property. Removing the coupling
        // (binding OutboxOptions into DI, or holding one inside MessagingOptions) is a public-surface
        // decision still open.
        services.Configure<MessagingOptions>(o =>
        {
            o.OutboxEnabled = builder.Options.OutboxEnabled;
            o.PollingIntervalSeconds = builder.Options.PollingIntervalSeconds;
            o.BatchSize = builder.Options.BatchSize;
            o.MaxRetries = builder.Options.MaxRetries;
            o.OutboxRetention = builder.Options.OutboxRetention;
            o.OutboxPurgeInterval = builder.Options.OutboxPurgeInterval;
            o.UseInMemoryDeadLetter = builder.Options.UseInMemoryDeadLetter;
            o.SubscriberName = builder.Options.SubscriberName;
        });

        // Register core services
        services.AddPragmaticJson();
        services.TryAddScoped<IMessageBus, InMemoryMessageBus>();
        services.TryAddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.TryAddSingleton<IBusResolver>(DefaultBusResolver.Instance);
        // Enumerable: each module assembly's SG-generated dispatch table adds itself alongside
        // this passthrough (which always returns null and is therefore harmless).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITypedMessageDispatchTable, PassthroughMessageDispatchTable>());

        // Dead letter store
        if (builder.Options.UseInMemoryDeadLetter)
        {
            services.TryAddSingleton<InMemoryDeadLetterStore>();
            services.TryAddSingleton<IDeadLetterStore>(sp => sp.GetRequiredService<InMemoryDeadLetterStore>());
        }

        // Named buses with isolated transports: wrap the default IMessageBus in the composite
        // so `bus.name`-addressed publishes reach the right bus. The keyed IMessageBus per name
        // stays directly resolvable too.
        if (builder.Options.NamedBuses.Count > 0)
            WrapInNamedBusComposite(services, [.. builder.Options.NamedBuses]);

        return services;
    }

    private static void WrapInNamedBusComposite(IServiceCollection services, string[] busNames)
    {
        var inner = services.LastOrDefault(d => d is { ServiceType: var t, IsKeyedService: false } && t == typeof(IMessageBus));
        if (inner is null)
            return;

        services.Remove(inner);
        services.Add(ServiceDescriptor.Describe(typeof(IMessageBus), sp =>
        {
            var defaultBus = (IMessageBus)ResolveDescriptor(sp, inner);
            var named = new Dictionary<string, IMessageBus>(StringComparer.Ordinal);
            foreach (var name in busNames)
            {
                if (sp.GetKeyedService<IMessageBus>(name) is { } bus)
                    named[name] = bus;
            }

            return named.Count == 0 ? defaultBus : new NamedBusMessageBus(defaultBus, named);
        }, inner.Lifetime));
    }

    private static object ResolveDescriptor(IServiceProvider sp, ServiceDescriptor descriptor)
        => descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(sp)
            ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
}
