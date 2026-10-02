using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Messaging.Configuration;

/// <summary>
///     Fluent builder for configuring messaging.
/// </summary>
public sealed class MessagingBuilder
{
    /// <summary>The underlying service collection.</summary>
    public IServiceCollection Services { get; }
    internal readonly MessagingOptions Options = new();

    internal MessagingBuilder(IServiceCollection services) => Services = services;

    /// <summary>
    ///     Names this application at the broker — its consumer group — instead of letting each module
    ///     be named by its own boundary.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Subscription names carry the subscriber so that two services consuming one event each get
    ///         a copy rather than dividing the messages; by default that name is the module's boundary.
    ///         Call this when the deployment's identity is not the module's: two deployments of one
    ///         module that must not share a queue, or two processes that must.
    ///     </para>
    ///     <para>
    ///         ⚠️ A subscription name is <b>operational state</b>. Changing it on a running deployment
    ///         leaves the previous queue bound and holding what it had, with nothing reading it.
    ///     </para>
    /// </remarks>
    public MessagingBuilder SubscribeAs(string subscriberName)
    {
        Options.SubscriberName = Ensure.Ensure.ThrowIfNullOrWhiteSpace(subscriberName);
        return this;
    }

    /// <summary>
    ///     Uses in-memory message bus (default for P0).
    /// </summary>
    public MessagingBuilder UseInMemory()
    {
        // InMemory is the default — this is a no-op marker for explicitness
        return this;
    }

    /// <summary>
    ///     Tunes the transactional outbox delivery/purge (polling, batch size, retries, retention).
    /// </summary>
    /// <remarks>
    ///     The outbox itself is wired per-boundary by the SG when a <c>[Boundary]</c> carries
    ///     <c>[EnableOutbox]</c> and the app references <c>Pragmatic.Messaging.EFCore</c>. This method
    ///     only configures the shared <see cref="MessagingOptions"/> the delivery pump and purge
    ///     service read; calling it without any <c>[EnableOutbox]</c> boundary has no effect.
    /// </remarks>
    public MessagingBuilder EnableOutbox(Action<OutboxOptions>? configure = null)
    {
        Options.OutboxEnabled = true;
        if (configure is not null)
        {
            var outboxOptions = new OutboxOptions();
            configure(outboxOptions);
            Options.PollingIntervalSeconds = outboxOptions.PollingIntervalSeconds;
            Options.BatchSize = outboxOptions.BatchSize;
            Options.MaxRetries = outboxOptions.MaxRetries;
            Options.OutboxRetention = outboxOptions.Retention;
            Options.OutboxPurgeInterval = outboxOptions.PurgeInterval;
        }

        return this;
    }

    /// <summary>
    ///     Disables the default in-memory dead letter store (when providing a custom one).
    /// </summary>
    public MessagingBuilder DisableInMemoryDeadLetter()
    {
        Options.UseInMemoryDeadLetter = false;
        return this;
    }

    /// <summary>
    ///     Enables idempotency (message deduplication) and schedules a periodic purge of expired
    ///     dedup records (default retention 7 days, purge every 6 hours) so the store does not
    ///     grow without bound.
    /// </summary>
    public MessagingBuilder EnableIdempotency(Action<IdempotencyOptions>? configure = null)
    {
        var options = new IdempotencyOptions();
        configure?.Invoke(options);

        Services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        Services.TryAddSingleton(options);
        Services.AddHostedService<IdempotencyPurgeService>();
        return this;
    }

    /// <summary>
    ///     Adds a named secondary bus with an ISOLATED transport.
    /// </summary>
    /// <param name="name">Bus name (e.g., "integration", "analytics").</param>
    /// <param name="configure">Bus configuration — call <see cref="BusBuilder.UseTransport"/>.</param>
    /// <remarks>
    ///     Handlers opt in with <c>[OnBus("{name}")]</c>: the SG emits the bus resolver and
    ///     per-bus subscriptions, this method materializes the keyed transport + a dedicated
    ///     consumer service, and <see cref="Extensions.MessagingServiceExtensions.AddPragmaticMessaging"/>
    ///     wraps the default <see cref="IMessageBus"/> in a <see cref="NamedBusMessageBus"/>
    ///     composite (publish to a named bus via the <c>bus.name</c> header, or resolve the keyed
    ///     <see cref="IMessageBus"/> directly).
    /// </remarks>
    public MessagingBuilder AddBus(string name, Action<BusBuilder> configure)
    {
        var busBuilder = new BusBuilder(name);
        configure(busBuilder);
        // Named bus registration stored for SG-generated bus resolution
        Options.NamedBuses.Add(name);

        if (busBuilder.TransportFactory is not { } transportFactory)
            return this; // resolver-only bus (no isolated transport): shares the default transport

        // Isolated transport + bus + consumer, all keyed by bus name.
        Services.AddKeyedSingleton<IMessageTransport>(name, (sp, _) => transportFactory(sp));
        Services.TryAddScoped<InMemoryMessageBus>();
        Services.TryAddSingleton<Routing.IMessageRouter, Routing.DefaultMessageRouter>();
        Services.AddKeyedScoped<IMessageBus>(name, (sp, _) => new TransportAwareMessageBus(
            sp.GetRequiredKeyedService<IMessageTransport>(name),
            sp.GetRequiredService<Routing.IMessageRouter>(),
            sp.GetRequiredService<IMessageSerializer>(),
            sp.GetRequiredService<InMemoryMessageBus>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TransportAwareMessageBus>>(),
            sp.GetService<IIdempotencyStore>(),
            sp.GetServices<IPartitionKeyResolver>()));
        Services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp => new NamedBusConsumerService(
            name,
            sp.GetRequiredKeyedService<IMessageTransport>(name),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<Routing.IMessageRouter>(),
            sp.GetServices<MessageSubscription>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NamedBusConsumerService>>(),
            sp.GetService<KillSwitchOptions>()));

        return this;
    }

    /// <summary>
    ///     Enables the consumer kill switch: after N consecutive dispatch failures on a
    ///     subscription, consumption pauses for a cool-down instead of hammering a broken
    ///     downstream. See <see cref="KillSwitchOptions"/>.
    /// </summary>
    public MessagingBuilder EnableKillSwitch(Action<KillSwitchOptions>? configure = null)
    {
        var options = new KillSwitchOptions();
        configure?.Invoke(options);
        Services.TryAddSingleton(options);
        return this;
    }

    /// <summary>
    ///     Enables saga orchestration support.
    /// </summary>
    public MessagingBuilder EnableSagas()
    {
        // Saga repositories are auto-registered by SG-generated SagaRegistrationTemplate
        return this;
    }

    /// <summary>
    ///     Enables EF Core-backed saga persistence so saga state survives process restarts. Opt in by
    ///     marking the saga's <c>[Boundary]</c> with <c>[EnableSagaPersistence]</c>: the generator then
    ///     maps the saga tables into that boundary's DbContext and registers
    ///     <c>EfCoreSagaRepository&lt;TSaga, TState&gt;</c> against it — entirely at compile time, no host
    ///     call required. This method is retained only for discoverability and is a no-op.
    /// </summary>
    public MessagingBuilder EnableSagaPersistence() => this;
}
