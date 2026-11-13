using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Persistence.EFCore.Interceptors;

/// <summary>
///     Runs <see cref="IEntityLifecycle{T}.OnSaving" /> for every entity a save adds or modifies, whichever path wrote it.
/// </summary>
/// <remarks>
///     <para>
///         The hook is "the last chance to modify the entity", and it ran only in the mutation invoker: a domain action,
///         a job or a seed saved past it, and a value derived on save was stale on every other path (#644). At the save
///         every path converges, as it does for the soft-delete mark and the audit stamps.
///     </para>
///     <para>
///         Registered after the stamps (audit, soft delete, owner, scope, tenant), so a hook reads them, and before the
///         roll-ups and the lifecycle events, so what they read is what the hook left. What a hook changes is picked up
///         by the save's own change detection, which runs after the interceptors, as the audit stamps are.
///     </para>
/// </remarks>
public sealed class EntitySavingHooksInterceptor(
    EntitySavingHooksRunner hooks,
    TimeProvider timeProvider,
    ICurrentUser? currentUser = null,
    ITenantContext? tenant = null) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Run(eventData);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Run(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Run(DbContextEventData eventData)
    {
        if (eventData.Context is null || hooks.IsEmpty)
            return;

        LifecycleContext? context = null;
        foreach (var entry in SaveEntries.All(eventData).ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            context ??= new LifecycleContext
            {
                Now = timeProvider.GetUtcNow(),
                UserId = currentUser?.IdOrNull(),
                TenantId = tenant?.TenantId
            };

            hooks.Run(entry.Entity, context);
        }
    }
}
