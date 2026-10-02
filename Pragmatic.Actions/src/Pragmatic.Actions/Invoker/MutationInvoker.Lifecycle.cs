using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;
using Pragmatic.Events;
using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Actions.Invoker;

public abstract partial class MutationInvoker<TMutation, TEntity>
{
    // =========================================================================
    // Lifecycle — Computed Defaults, Presets, Domain Events, Cache Invalidation
    // =========================================================================

    /// <summary>
    ///     Applies computed default values to a newly created entity.
    ///     Override in generated invokers when entity has [ComputedDefault] properties.
    /// </summary>
    protected virtual Task ApplyComputedDefaultsAsync(TEntity entity, LifecycleContext context, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    ///     Creates preset child entities after the main entity is persisted.
    ///     Override in generated invokers when entity has [HasPresets] + [PresetProvider].
    /// </summary>
    protected virtual Task ApplyPresetsAsync(TEntity entity, LifecycleContext context, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    ///     Invokes every registered <see cref="IEntityLifecycle{T}"/> for the entity — the
    ///     <see cref="IEntityLifecycle{T}.OnCreating"/> hook. Called once, after construction and default
    ///     application, before validation. Resolved generically from DI, so it is a no-op when nothing
    ///     is registered.
    /// </summary>
    private void InvokeOnCreatingHooks(TEntity entity, LifecycleContext context)
    {
        foreach (var hook in _serviceProvider.GetServices<IEntityLifecycle<TEntity>>())
            hook.OnCreating(entity, context);
    }

    /// <summary>
    ///     Invokes every registered <see cref="IEntityLifecycle{T}.OnSaving"/> hook — the last chance to
    ///     modify the entity, after validation and before persistence. No-op when none registered.
    /// </summary>
    private void InvokeOnSavingHooks(TEntity entity, LifecycleContext context)
    {
        foreach (var hook in _serviceProvider.GetServices<IEntityLifecycle<TEntity>>())
            hook.OnSaving(entity, context);
    }

    /// <summary>
    ///     Builds a lifecycle context from available services (clock, user, tenant).
    /// </summary>
    private LifecycleContext BuildLifecycleContext()
    {
        var currentUser = _serviceProvider.GetService<Pragmatic.Identity.ICurrentUser>();
        var tenantContext = _serviceProvider.GetService<Pragmatic.MultiTenancy.ITenantContext>();
        var timeProvider = _serviceProvider.GetService<TimeProvider>();

        return new LifecycleContext
        {
            Now = timeProvider?.GetUtcNow() ?? DateTimeOffset.UtcNow,
            UserId = currentUser is { IsAuthenticated: true } ? currentUser.Id : null,
            TenantId = tenantContext?.TenantId
        };
    }

    /// <summary>
    ///     Dispatches domain events if the entity implements <see cref="IHasDomainEvents" />.
    /// </summary>
    private async Task DispatchDomainEventsAsync(TEntity entity, CancellationToken ct)
    {
        if (entity is IHasDomainEvents { DomainEvents.Count: > 0 } eventSource)
        {
            var dispatcher = _serviceProvider.GetService<IDomainEventDispatcher>();
            if (dispatcher is not null)
            {
                await dispatcher.DispatchAsync(eventSource.DomainEvents, ct).ConfigureAwait(false);
                eventSource.ClearDomainEvents();
            }
        }
    }

    /// <summary>
    ///     Performs cache invalidation if the mutation implements <see cref="ICacheInvalidator"/>,
    ///     against the stacks its category is routed to. Skips, loudly, when caching is not registered.
    /// </summary>
    /// <remarks>
    ///     The routing itself is <see cref="Pragmatic.Actions.Cache.CacheInvalidation"/>, shared with the
    ///     action invokers, so that actions run it as well as mutations.
    ///     What stays local is the log — the mutation asked to invalidate and no
    ///     <c>ICacheStack</c> is registered, said in this invoker's own voice rather than serving stale
    ///     entries in silence.
    /// </remarks>
    private Task InvalidateCacheAsync(TMutation mutation, CancellationToken ct)
        => Pragmatic.Actions.Cache.CacheInvalidation.RunAsync(
            mutation,
            _serviceProvider,
            () => LogCacheInvalidationSkipped(typeof(TMutation).Name),
            ct);

    /// <summary>
    ///     Runs the post-commit side effects (entity domain-event dispatch, <c>[Raises&lt;T&gt;]</c>
    ///     dispatch, cache invalidation) after a successful commit. Isolated: the commit already
    ///     happened, so a failure here must NOT surface to the caller as a thrown failure — that would
    ///     make the caller retry and double-run a committed mutation. Logged at Error instead.
    /// </summary>
    private async Task RunPostCommitSideEffectsAsync(
        TMutation mutation, TEntity entity,
        IReadOnlyList<IDomainEvent> declaredEvents, string mutationType, CancellationToken ct)
    {
        try
        {
            // Read while the claim is intact; used after stepping outside it. A mutation can be the root
            // of a chain too — invoked straight from an endpoint, with other mutations of the same
            // boundary nested under it — and whatever they deferred is flushed here.
            var batch = global::Pragmatic.Actions.Commit.CommitScope.BatchFor(UnitOfWork);

            // Outside every claim, because the commit has happened. A handler that writes through this
            // same unit of work is its own operation now; left inside, it was read as one more nested
            // step and staged writes nobody would ever save. See CommitScope.Suspend.
            using var outsideTheCommit = global::Pragmatic.Actions.Commit.CommitScope.Suspend();

            if (entity is IHasDomainEvents { DomainEvents.Count: > 0 } evented)
                LogDispatchingEvents(mutationType, evented.DomainEvents.Count);
            await DispatchDomainEventsAsync(entity, ct).ConfigureAwait(false);
            await DispatchRaisedEventsAsync(declaredEvents, ct).ConfigureAwait(false);

            await global::Pragmatic.Actions.Commit.DeferredEventFlush
                .FlushAsync(batch, _serviceProvider, ct)
                .ConfigureAwait(false);

            if (mutation is ICacheInvalidator)
                LogInvalidatingCache(mutationType);
            await InvalidateCacheAsync(mutation, ct).ConfigureAwait(false);
        }
        catch (Exception postCommitEx)
        {
            LogPostCommitSideEffectFailed(mutationType, postCommitEx);
        }
    }
}
