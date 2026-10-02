using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Events.EFCore.Outbox;

/// <summary>
///     Wiring for the transactional event outbox.
/// </summary>
/// <remarks>
///     Three steps in the consumer application:
///     <list type="number">
///         <item><description>
///             <c>OnModelCreating</c> → <see cref="AddEventOutbox(ModelBuilder)"/> to map the table.
///         </description></item>
///         <item><description>
///             DbContext options → <c>AddInterceptors(sp.GetRequiredService&lt;EventOutboxInterceptor&gt;())</c>.
///         </description></item>
///         <item><description>
///             services → <see cref="AddEventOutbox{TContext}"/> to register the interceptor,
///             options, and the delivery background service.
///         </description></item>
///     </list>
/// </remarks>
public static class EventOutboxExtensions
{
    /// <summary>Maps the <see cref="EventOutboxEntry"/> table. Call from <c>OnModelCreating</c>.</summary>
    public static ModelBuilder AddEventOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new EventOutboxEntryConfiguration());
        return modelBuilder;
    }

    /// <summary>
    ///     Registers the transactional event outbox: the capture interceptor, options,
    ///     and the <see cref="EventOutboxDeliveryService{TContext}"/> background delivery loop.
    /// </summary>
    /// <typeparam name="TContext">The application DbContext that owns the outbox table.</typeparam>
    /// <remarks>
    ///     Calling this method more than once for the same <typeparamref name="TContext"/> is a no-op:
    ///     the delivery background service is registered only once to prevent duplicate delivery loops.
    /// </remarks>
    public static IServiceCollection AddEventOutbox<TContext>(
        this IServiceCollection services,
        Action<EventOutboxOptions>? configure = null)
        where TContext : DbContext
    {
        var options = new EventOutboxOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<EventOutboxInterceptor>();

        // Build a closed allowlist of resolvable event types from the registered handlers.
        // This replaces an unsafe Type.GetType(dbString) in the delivery loop (gadget surface)
        // with a fail-closed lookup: only events the app actually handles are deserializable.
        services.TryAddSingleton<IEventOutboxTypeResolver>(_ =>
            new EventOutboxTypeResolver(ResolveAllowedEventTypes(services)));

        // One delivery pass, reachable by name. The background loop below calls exactly this, and so
        // can the application: an outbox whose only trigger is a five-second timer can be asserted
        // about only by waiting for it, and a test that waits for a scheduler is asserting the
        // scheduler.
        services.TryAddSingleton<IEventOutboxDrainer<TContext>, EventOutboxDrainer<TContext>>();

        // Guard against duplicate delivery loops when AddEventOutbox is called more than once.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, EventOutboxDeliveryService<TContext>>());

        return services;
    }

    /// <summary>
    ///     Derives the closed set of allowlisted event types from every registered
    ///     <see cref="IDomainEventHandler{TEvent}"/> service descriptor's generic argument.
    ///     This is a compile-time-determined set (no arbitrary type loading from the DB).
    /// </summary>
    private static IEnumerable<Type> ResolveAllowedEventTypes(IServiceCollection services)
    {
        var handlerInterface = typeof(Pragmatic.Events.IDomainEventHandler<>);
        var eventTypes = new HashSet<Type>();

        foreach (var descriptor in services)
        {
            var serviceType = descriptor.ServiceType;
            if (serviceType.IsGenericType
                && serviceType.GetGenericTypeDefinition() == handlerInterface)
            {
                eventTypes.Add(serviceType.GetGenericArguments()[0]);
            }
        }

        return eventTypes;
    }
}
