using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Events.Outbox;

/// <summary>
///     Default <see cref="IEventOutboxTypeResolver"/> backed by an immutable allowlist.
///     Built once at startup from the set of domain-event types the application handles, so the
///     dangerous <c>Type.GetType(dbString)</c> path is never taken.
/// </summary>
public sealed class EventOutboxTypeResolver : IEventOutboxTypeResolver
{
    private readonly Dictionary<string, Type> _byName;

    /// <summary>
    ///     Builds the allowlist. Each allowed type is indexed by its assembly-qualified name,
    ///     full name, and simple name so any form an outbox row holds resolves.
    /// </summary>
    /// <param name="allowedEventTypes">The closed set of resolvable domain-event types.</param>
    public EventOutboxTypeResolver(IEnumerable<Type> allowedEventTypes)
    {
        ArgumentNullException.ThrowIfNull(allowedEventTypes);

        _byName = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var type in allowedEventTypes)
        {
            // Last writer wins is fine: a collision means two forms map to the same type set;
            // we index every form a capture might have persisted (AQN / FullName / Name).
            if (type.AssemblyQualifiedName is { } aqn)
                _byName[aqn] = type;
            if (type.FullName is { } full)
                _byName[full] = type;
            _byName[type.Name] = type;
        }
    }

    /// <summary>
    ///     The allowlist of the event types <paramref name="services" /> registers an
    ///     <see cref="IDomainEventHandler{TEvent}"/> for: every handler descriptor's generic argument, a
    ///     set fixed when the services are built (no arbitrary type loading from the database).
    /// </summary>
    public static EventOutboxTypeResolver FromHandlers(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var handlerInterface = typeof(IDomainEventHandler<>);
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

        return new EventOutboxTypeResolver(eventTypes);
    }

    /// <inheritdoc />
    public Type? Resolve(string storedTypeName)
        => storedTypeName is not null && _byName.TryGetValue(storedTypeName, out var type)
            ? type
            : null;
}
