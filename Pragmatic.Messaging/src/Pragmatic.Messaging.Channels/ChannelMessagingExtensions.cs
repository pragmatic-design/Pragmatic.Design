using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.Channels;

/// <summary>
///     Extension methods for configuring Channel transport on <see cref="MessagingBuilder"/>.
/// </summary>
public static class ChannelMessagingExtensions
{
    /// <summary>
    ///     Uses System.Threading.Channels as the in-process message transport.
    ///     Provides async decoupling with configurable backpressure.
    /// </summary>
    public static MessagingBuilder UseChannels(
        this MessagingBuilder builder,
        Action<ChannelOptions>? configure = null)
    {
        var options = new ChannelOptions();
        configure?.Invoke(options);

        // Register transport (factory: IDeadLetterStore is optional — resolved only if registered)
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ChannelTransport>(sp => new ChannelTransport(
            sp.GetRequiredService<ChannelOptions>(),
            sp.GetRequiredService<ILogger<ChannelTransport>>(),
            sp.GetService<IDeadLetterStore>()));
        builder.Services.AddSingleton<IMessageTransport>(sp => sp.GetRequiredService<ChannelTransport>());

        // Host health: without this the aggregator the generated host registers collects nothing,
        // and the messaging half of host health silently reports no transports at all.
        builder.Services.AddSingleton<global::Pragmatic.ControlPlane.IHostHealthContributor>(
            sp => new global::Pragmatic.Messaging.Diagnostics.TransportHealthContributor(sp.GetRequiredService<ChannelTransport>()));

        // Register transport-aware bus (replaces InMemoryMessageBus as IMessageBus)
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

        // Register router (default convention-based; SG-generated overrides via TryAdd)
        builder.Services.TryAddSingleton<IMessageRouter, DefaultMessageRouter>();

        // Register consumer background service
        builder.Services.AddHostedService<ChannelConsumerService>();

        return builder;
    }
}
