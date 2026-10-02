using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.Sql;

/// <summary>
///     Extension methods for configuring the SQL transport on <see cref="MessagingBuilder"/>.
/// </summary>
public static class SqlMessagingExtensions
{
    /// <summary>
    ///     Uses PostgreSQL or SQL Server tables as the message transport — no broker: durable
    ///     queues with lease-based competing consumers, native delayed messages and RESTART-SAFE
    ///     schedule cancellation, dead-letter table after MaxDeliveryCount, and pg_notify
    ///     low-latency wakeups on PostgreSQL. The provider comes from
    ///     <see cref="SqlTransportOptions.ConfigureDbContext"/> (e.g. <c>o => o.UseNpgsql(cs)</c>).
    /// </summary>
    public static MessagingBuilder UseSqlTransport(
        this MessagingBuilder builder,
        Action<SqlTransportOptions> configure)
    {
        var options = new SqlTransportOptions { ConfigureDbContext = null! };
        configure(options);

        if (options.ConfigureDbContext is null)
            throw new ArgumentException("SqlTransportOptions.ConfigureDbContext is required (e.g. o => o.UseNpgsql(connectionString)).");

        // Register transport infrastructure (factory: the transport is a singleton with loops)
        builder.Services.AddSingleton(options);
        builder.Services.AddDbContextFactory<SqlTransportDbContext>((_, db) => options.ConfigureDbContext(db));
        builder.Services.AddSingleton<SqlTransportStorage>();
        builder.Services.AddSingleton<SqlTransportSchema>();
        builder.Services.AddSingleton<SqlTransport>();
        builder.Services.AddSingleton<IMessageTransport>(sp => sp.GetRequiredService<SqlTransport>());

        // Host health: without this the aggregator the generated host registers collects nothing,
        // and the messaging half of host health silently reports no transports at all.
        builder.Services.AddSingleton<global::Pragmatic.ControlPlane.IHostHealthContributor>(
            sp => new global::Pragmatic.Messaging.Diagnostics.TransportHealthContributor(sp.GetRequiredService<SqlTransport>()));

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

        // Native scheduler on VisibleAt — durable, restart-safe cancel (TryAdd: explicit wins)
        builder.Services.TryAddSingleton<IMessageScheduler, SqlMessageScheduler>();

        // Consumer service: connects the transport at startup and binds all registered subscriptions.
        builder.Services.AddHostedService<SqlConsumerService>();

        return builder;
    }
}
