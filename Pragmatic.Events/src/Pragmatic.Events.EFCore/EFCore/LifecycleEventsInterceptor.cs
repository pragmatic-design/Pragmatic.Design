using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Events.EFCore;

/// <summary>
///     EF Core interceptor that raises lifecycle domain events declared via
///     <c>[Raises&lt;TEvent&gt;(on: ...)]</c>. During <c>SavingChanges</c> — while the change-tracking
///     state is still readable — it maps each tracked <see cref="IRaisesLifecycleEvents"/> entity's
///     <see cref="EntityState"/> to an <see cref="EntityLifecycle"/> and asks the entity to raise the
///     matching events.
/// </summary>
/// <remarks>
///     <para>
///         A soft delete reaches EF as a <see cref="EntityState.Modified"/> whose <c>IsDeleted</c> flag
///         flips to <c>true</c>; it is mapped to <see cref="EntityLifecycle.Deleted"/>, not Updated.
///     </para>
///     <para>
///         <b>It raises and stops there.</b> What dispatches is <c>EfCoreUnitOfWork</c>, once the save
///         has succeeded and in the scope that asked for the write. This runs during
///         <c>SavingChanges</c>, so it must still be registered before the outbox capture interceptors:
///         they read the entity's events at that point, and anything raised after them never reaches
///         the outbox.
///     </para>
/// </remarks>
public sealed class LifecycleEventsInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) RaiseLifecycleEvents(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) RaiseLifecycleEvents(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void RaiseLifecycleEvents(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not IRaisesLifecycleEvents raiser)
                continue;

            EntityLifecycle? lifecycle = entry.State switch
            {
                EntityState.Added => EntityLifecycle.Created,
                EntityState.Deleted => EntityLifecycle.Deleted,
                EntityState.Modified => IsSoftDeleting(entry) ? EntityLifecycle.Deleted : EntityLifecycle.Updated,
                _ => null
            };

            if (lifecycle is { } lc)
                raiser.RaiseLifecycleEvents(lc);
        }
    }

    // A soft delete is a Modified entry on an ISoftDelete entity whose IsDeleted flag was just set to true.
    // Gating on ISoftDelete + nameof keeps this from misfiring on an unrelated "IsDeleted" column and from
    // drifting if the interface member is ever renamed. The TRANSITION is what matters: a flag that was
    // already true and is merely written again (whole-entity update, re-attached graph) is an update, not
    // a delete — checking IsModified/CurrentValue alone would re-raise the Deleted event on every save.
    private static bool IsSoftDeleting(EntityEntry entry)
    {
        if (entry.Entity is not ISoftDelete)
            return false;

        var deletedFlag = entry.Properties.FirstOrDefault(p => p.Metadata.Name == nameof(ISoftDelete.IsDeleted));
        return deletedFlag is { IsModified: true, CurrentValue: true, OriginalValue: false };
    }
}
