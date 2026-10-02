using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.Kafka;

/// <summary>
///     Extension methods for configuring Kafka transport on <see cref="MessagingBuilder"/>.
/// </summary>
public static class KafkaMessagingExtensions
{
    /// <summary>
    ///     Uses Apache Kafka as the message transport for streaming and event-driven scenarios.
    /// </summary>
    public static MessagingBuilder UseKafka(
        this MessagingBuilder builder,
        Action<KafkaOptions> configure)
    {
        var options = new KafkaOptions { BootstrapServers = "" };
        configure(options);

        if (string.IsNullOrEmpty(options.BootstrapServers))
            throw new ArgumentException("Kafka BootstrapServers is required.");

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<KafkaTransport>();
        builder.Services.AddSingleton<IMessageTransport>(sp => sp.GetRequiredService<KafkaTransport>());

        // Host health: without this the aggregator the generated host registers collects nothing,
        // and the messaging half of host health silently reports no transports at all.
        builder.Services.AddSingleton<global::Pragmatic.ControlPlane.IHostHealthContributor>(
            sp => new global::Pragmatic.Messaging.Diagnostics.TransportHealthContributor(sp.GetRequiredService<KafkaTransport>()));

        // TryAdd so we do not overwrite a TransportAwareMessageBus that core messaging
        // may have already registered (e.g., when UseKafka is called after UseChannels).
        builder.Services.TryAddScoped<InMemoryMessageBus>();
        builder.Services.TryAddScoped<IMessageBus>(sp =>
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

        builder.Services.TryAddSingleton<IMessageRouter, DefaultMessageRouter>();

        // Consumer service: connects the transport at startup and binds all registered subscriptions.
        // Without it KafkaTransport stays Disconnected and every publish/subscribe throws.
        builder.Services.AddHostedService<KafkaConsumerService>();

        return builder;
    }
}
