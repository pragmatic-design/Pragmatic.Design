namespace Pragmatic.Events.EFCore.Outbox;

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
    ///     full name, and simple name so any form written by <c>EventOutboxInterceptor</c> resolves.
    /// </summary>
    /// <param name="allowedEventTypes">The closed set of resolvable domain-event types.</param>
    public EventOutboxTypeResolver(IEnumerable<Type> allowedEventTypes)
    {
        ArgumentNullException.ThrowIfNull(allowedEventTypes);

        _byName = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var type in allowedEventTypes)
        {
            // Last writer wins is fine: a collision means two forms map to the same type set;
            // we index every form the interceptor might have persisted (AQN / FullName / Name).
            if (type.AssemblyQualifiedName is { } aqn)
                _byName[aqn] = type;
            if (type.FullName is { } full)
                _byName[full] = type;
            _byName[type.Name] = type;
        }
    }

    /// <inheritdoc />
    public Type? Resolve(string storedTypeName)
        => storedTypeName is not null && _byName.TryGetValue(storedTypeName, out var type)
            ? type
            : null;
}
