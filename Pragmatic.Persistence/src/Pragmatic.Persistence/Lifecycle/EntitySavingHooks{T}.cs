using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Persistence.Lifecycle;

/// <summary>
///     The <see cref="IEntitySavingHooks" /> of <typeparamref name="T" />: the hooks registered as
///     <see cref="IEntityLifecycle{T}" />, however they were registered, generated or by hand.
/// </summary>
/// <remarks>
///     Stateless, so registered once as a singleton: the hooks are resolved from the save's scope at the save, which
///     keeps a scoped hook scoped and costs nothing for a type no save writes.
/// </remarks>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class EntitySavingHooks<T> : IEntitySavingHooks
    where T : class
{
    /// <inheritdoc />
    public Type EntityType => typeof(T);

    /// <inheritdoc />
    public void Run(IServiceProvider services, object entity, LifecycleContext context)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var hook in services.GetServices<IEntityLifecycle<T>>())
            hook.OnSaving((T)entity, context);
    }
}
