using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.AzureServiceBus;

/// <summary>
///     Extension methods for configuring the Azure Service Bus transport on <see cref="MessagingBuilder"/>.
/// </summary>
public static class AzureServiceBusMessagingExtensions
{
    /// <summary>
    ///     Uses Azure Service Bus as the message transport for distributed messaging.
    ///     Publish = topic (fan-out), Send = queue; failed handlers are abandoned and ASB
    ///     dead-letters natively after MaxDeliveryCount. Also registers the broker-native
    ///     <see cref="IMessageScheduler"/> (scheduled enqueue).
    /// </summary>
    public static MessagingBuilder UseAzureServiceBus(
        this MessagingBuilder builder,
        Action<AzureServiceBusOptions> configure)
    {
        var options = new AzureServiceBusOptions { ConnectionString = "" };
        configure(options);

        if (string.IsNullOrEmpty(options.ConnectionString))
            throw new ArgumentException("Azure Service Bus ConnectionString is required.");

        // Register transport
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<AzureServiceBusTransport>();
        builder.Services.AddSingleton<IMessageTransport>(sp => sp.GetRequiredService<AzureServiceBusTransport>());

        // Host health: without this the aggregator the generated host registers collects nothing,
        // and the messaging half of host health silently reports no transports at all.
        builder.Services.AddSingleton<global::Pragmatic.ControlPlane.IHostHealthContributor>(
            sp => new global::Pragmatic.Messaging.Diagnostics.TransportHealthContributor(sp.GetRequiredService<AzureServiceBusTransport>()));

        // Register transport-aware bus
        builder.Services.AddScoped<InMemoryMessageBus>();
        builder.Services.AddScoped<IMessageBus>(sp =>
        {
            var transport = sp.GetRequiredService<IMessageTransport>();
            var router = sp.GetRequiredService<IMessageRouter>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var localBus = sp.GetRequiredService<InMemoryMessageBus>();
            var logger = sp.GetRequiredService<ILogger<TransportAwareMessageBus>>();
            // Idempotency store + partition-key resolvers are optional opt-ins: without
            // passing them here the consume-side dedup and [PartitionKey] routing never engage.
            return new TransportAwareMessageBus(transport, router, serializer, localBus, logger,
                sp.GetService<IIdempotencyStore>(),
                sp.GetServices<IPartitionKeyResolver>(),
                sp.GetService<Pragmatic.Messaging.RequestReply.TransportReplyChannel>(),
                sp.GetService<IClaimCheckStore>(),
                sp.GetService<Pragmatic.Messaging.Configuration.ClaimCheckOptions>(),
                sp.GetService<Microsoft.Extensions.Options.IOptions<Pragmatic.Messaging.Configuration.MessagingOptions>>()?.Value);
        });

        // Requester side of distributed request/reply (lazy reply subscription).
        builder.Services.TryAddSingleton(sp => new Pragmatic.Messaging.RequestReply.TransportReplyChannel(
            sp.GetRequiredService<IMessageTransport>(),
            sp.GetRequiredService<ILogger<Pragmatic.Messaging.RequestReply.TransportReplyChannel>>()));

        // Register router (SG-generated takes precedence via TryAdd)
        builder.Services.TryAddSingleton<IMessageRouter, DefaultMessageRouter>();

        // Broker-native scheduled messages (TryAdd: an explicitly registered scheduler wins).
        // Factory: IScheduleHandleStore is an optional opt-in (EnableEfCorePersistence) that
        // makes cancel restart-safe — DI can't resolve optional ctor params on its own.
        builder.Services.TryAddSingleton<IMessageScheduler>(sp => new AzureServiceBusMessageScheduler(
            sp.GetRequiredService<AzureServiceBusTransport>(),
            sp.GetRequiredService<IMessageRouter>(),
            sp.GetRequiredService<IMessageSerializer>(),
            sp.GetRequiredService<ILogger<AzureServiceBusMessageScheduler>>(),
            sp.GetService<IScheduleHandleStore>()));

        // Consumer service: connects the transport at startup and binds all registered subscriptions.
        builder.Services.AddHostedService<AzureServiceBusConsumerService>();

        return builder;
    }
}
