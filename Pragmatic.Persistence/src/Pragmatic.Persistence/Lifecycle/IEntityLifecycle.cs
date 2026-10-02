namespace Pragmatic.Persistence.Lifecycle;

/// <summary>
///     Entity lifecycle hooks for creation, saving, and event generation.
///     All methods have default implementations (no-op) — implement only what you need.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public interface IEntityLifecycle<in T> where T : class
{
    /// <summary>
    ///     Called after entity construction — set default values.
    ///     Runs before validation.
    /// </summary>
    void OnCreating(T entity, LifecycleContext context) { }

    /// <summary>
    ///     Called before saving — last chance to modify the entity.
    ///     Runs after validation.
    /// </summary>
    void OnSaving(T entity, LifecycleContext context) { }
}
