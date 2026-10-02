using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.RabbitMQ;

/// <summary>
///     Extension methods for configuring RabbitMQ transport on <see cref="MessagingBuilder"/>.
/// </summary>
public static class RabbitMqMessagingExtensions
{
    /// <summary>
    ///     Uses RabbitMQ as the message transport for distributed messaging.
    /// </summary>
    public static MessagingBuilder UseRabbitMq(
        this MessagingBuilder builder,
        Action<RabbitMqOptions> configure)
    {
        var options = new RabbitMqOptions { ConnectionString = "" };
        configure(options);

        if (string.IsNullOrEmpty(options.ConnectionString))
            throw new ArgumentException("RabbitMQ ConnectionString is required.");

        // Register transport
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<RabbitMqTransport>();
        builder.Services.AddSingleton<IMessageTransport>(sp => sp.GetRequiredService<RabbitMqTransport>());

        // Host health: without this the aggregator the generated host registers collects nothing,
        // and the messaging half of host health silently reports no transports at all.
        builder.Services.AddSingleton<global::Pragmatic.ControlPlane.IHostHealthContributor>(
            sp => new global::Pragmatic.Messaging.Diagnostics.TransportHealthContributor(sp.GetRequiredService<RabbitMqTransport>()));

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

        // Consumer service: connects the transport at startup and binds all registered subscriptions.
        // Without it RabbitMqTransport stays Disconnected and every publish/subscribe throws.
        builder.Services.AddHostedService<RabbitMqConsumerService>();

        return builder;
    }
}
