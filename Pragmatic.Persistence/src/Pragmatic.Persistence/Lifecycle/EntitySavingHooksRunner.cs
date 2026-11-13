namespace Pragmatic.Persistence.Lifecycle;

/// <summary>
///     Runs the <c>OnSaving</c> hooks of an entity a save writes, by the entity's type: what every engine's save calls.
/// </summary>
/// <param name="services">The save's scope, which the hooks are resolved from.</param>
/// <param name="adapters">One per entity type, registered by the generated persistence registration.</param>
public sealed class EntitySavingHooksRunner(IServiceProvider services, IEnumerable<IEntitySavingHooks> adapters)
{
    private readonly Dictionary<Type, IEntitySavingHooks> _byType = Index(adapters);

    /// <summary>Whether no entity type has hooks registered, so a save has nothing to run.</summary>
    public bool IsEmpty => _byType.Count == 0;

    /// <summary>Runs the hooks of <paramref name="entity" />'s type; nothing for a type with none.</summary>
    public void Run(object entity, LifecycleContext context)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (_byType.TryGetValue(entity.GetType(), out var hooks))
            hooks.Run(services, entity, context);
    }

    /// <summary>One adapter per type: an entity registered by two registrations has its hooks run once.</summary>
    private static Dictionary<Type, IEntitySavingHooks> Index(IEnumerable<IEntitySavingHooks> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        var byType = new Dictionary<Type, IEntitySavingHooks>();
        foreach (var adapter in adapters)
            byType.TryAdd(adapter.EntityType, adapter);
        return byType;
    }
}
