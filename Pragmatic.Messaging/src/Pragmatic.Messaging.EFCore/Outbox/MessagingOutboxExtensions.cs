using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging.Entities;

namespace Pragmatic.Messaging.EFCore.Outbox;

/// <summary>
///     Wiring for the Messaging transactional outbox — the <b>transport-publish</b> variant of the
///     outbox pattern: domain events captured in the same transaction as the entity change are later
///     published to the configured transport (RabbitMQ/Kafka/ASB/Sql/Channels) via
///     <see cref="IMessageBus"/>. (The in-process dispatch variant is the Events
///     <c>[EnableEventOutbox]</c> — a boundary picks one; both capture and clear the same domain
///     events, so enabling both is a mistake the SG flags.)
/// </summary>
/// <remarks>
///     The <c>[EnableOutbox]</c> boundary attribute drives the SG to emit both calls per boundary:
///     <see cref="AddMessagingOutbox(ModelBuilder)"/> in the generated DbContext's
///     <c>OnModelCreating</c>, and <see cref="AddMessagingOutbox{TContext}"/> in the host DI
///     registration (which also adds the capture interceptor to the context options).
/// </remarks>
public static class MessagingOutboxExtensions
{
    /// <summary>Maps the <c>__OutboxMessages</c> table. Call from <c>OnModelCreating</c>.</summary>
    public static ModelBuilder AddMessagingOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxEntityTypeConfiguration());
        return modelBuilder;
    }

    /// <summary>
    ///     Registers the Messaging transactional outbox for <typeparamref name="TContext"/>: the
    ///     EF-backed <see cref="IOutboxSource"/> for this boundary, the capture interceptor, and —
    ///     once, regardless of how many boundaries opt in — the delivery pump and the retention
    ///     purge service.
    /// </summary>
    /// <typeparam name="TContext">The generated boundary DbContext that owns the outbox table.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="boundaryName">The boundary name, surfaced on <see cref="IOutboxSource.BoundaryName"/>.</param>
    /// <remarks>
    ///     Call once per boundary. The interceptor, delivery pump and purge service are registered
    ///     idempotently (<c>TryAdd*</c>); only the per-boundary source is added each call, so the
    ///     single non-generic pump drains every boundary's table.
    /// </remarks>
    public static IServiceCollection AddMessagingOutbox<TContext>(
        this IServiceCollection services,
        string boundaryName)
        where TContext : DbContext
    {
        // Capture interceptor — resolved by the generated DbContext options lambda and added to the
        // context's interceptors (writes __OutboxMessages rows in the same transaction as the change).
        services.TryAddSingleton<OutboxInterceptor>();

        // One source per boundary (distinct TContext + name). AddScoped — NOT TryAddEnumerable —
        // because every source shares the EfCoreOutboxSource implementation type; TryAddEnumerable
        // would collapse all boundaries to one. The SG emits this exactly once per boundary.
        services.AddScoped<IOutboxSource>(sp => new EfCoreOutboxSource(
            sp.GetRequiredService<TContext>(),
            boundaryName,
            sp.GetRequiredService<ILogger<EfCoreOutboxSource>>()));

        // Single delivery pump and purge service, deduped across boundaries: both iterate every
        // registered IOutboxSource, so one instance covers all boundaries.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxDeliveryService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxPurgeService>());

        // Host health, deduped for the same reason as the pump: one contributor iterates every
        // registered IOutboxSource. It existed but was never registered, so HostHealthAggregator —
        // which the generated host DOES register — reported no outbox at all.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Pragmatic.ControlPlane.IHostHealthContributor, OutboxHealthContributor>());

        return services;
    }
}
