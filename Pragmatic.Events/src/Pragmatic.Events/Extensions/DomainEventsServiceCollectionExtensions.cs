using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Events.Extensions;

/// <summary>
///     Extension methods for registering domain events infrastructure.
/// </summary>
public static class DomainEventsServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds the in-memory domain event dispatcher to the service collection.
        /// </summary>
        /// <returns>The service collection for chaining.</returns>
        /// <remarks>
        ///     <para>
        ///         The in-memory dispatcher is suitable for development, testing,
        ///         and simple scenarios. For production systems requiring reliability,
        ///         consider using an outbox pattern with a message broker.
        ///     </para>
        /// </remarks>
        public IServiceCollection AddInMemoryDomainEvents()
        {
            services.TryAddScoped<IDomainEventDispatcher, InMemoryEventDispatcher>();
            // Typed dispatch tables are contributed additively by each module's generated
            // AddPragmaticEventHandlers() (see EventDispatchTableTemplate). When no module
            // references the source generator the dispatcher resolves an empty set and the
            // untyped batch path uses a dynamic fallback — so no default table is registered here.
            return services;
        }

        /// <summary>
        ///     Registers a domain event handler.
        /// </summary>
        /// <typeparam name="THandler">The handler type.</typeparam>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddDomainEventHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler, TEvent>()
            where THandler : class, IDomainEventHandler<TEvent>
            where TEvent : IDomainEvent
        {
            services.AddScoped<IDomainEventHandler<TEvent>, THandler>();
            return services;
        }

            }
}
