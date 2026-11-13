namespace Pragmatic.Persistence.Lifecycle;

/// <summary>
///     The <see cref="IEntityLifecycle{T}.OnSaving" /> hooks of one entity type, callable with the entity as an
///     <see cref="object" />: what a save holds, whatever the engine.
/// </summary>
/// <remarks>
///     <para>
///         A save sees entries of every type, untyped. Resolving <c>IEntityLifecycle&lt;T&gt;</c> for a runtime type would
///         be <c>MakeGenericType</c>, so the generated persistence registration registers one typed
///         <see cref="EntitySavingHooks{T}" /> per entity instead, and <see cref="EntitySavingHooksRunner" /> picks it by type.
///     </para>
///     <para>
///         Every engine runs them at the save, for each added or modified entity: EF Core through
///         <c>EntitySavingHooksInterceptor</c>, the Sql and documents engines in their save phases. So a hook runs on a
///         mutation, a domain action, a job or a seed alike, once.
///     </para>
/// </remarks>
public interface IEntitySavingHooks
{
    /// <summary>The entity type whose hooks this runs.</summary>
    Type EntityType { get; }

    /// <summary>Runs every registered <see cref="IEntityLifecycle{T}" />'s <c>OnSaving</c> on <paramref name="entity" />.</summary>
    /// <param name="services">The save's scope, which the hooks are resolved from.</param>
    /// <param name="entity">The entity being saved, of <see cref="EntityType" />.</param>
    /// <param name="context">The save's instant, caller and tenant.</param>
    void Run(IServiceProvider services, object entity, LifecycleContext context);
}
