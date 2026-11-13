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
    ///     Called at the save, for every added or modified entity of the type — last chance to modify it.
    /// </summary>
    /// <remarks>
    ///     Whatever wrote the entity (a mutation, a domain action, a job, a seed) and on every engine: the save runs it, once,
    ///     after the audit, owner, scope and tenant stamps and before the lifecycle events. Inside a mutation that is after
    ///     its validation.
    /// </remarks>
    void OnSaving(T entity, LifecycleContext context) { }
}
