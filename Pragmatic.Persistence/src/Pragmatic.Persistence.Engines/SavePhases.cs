using Pragmatic.Authorization;
using Pragmatic.Events;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Scopes;

namespace Pragmatic.Persistence.Engines;

/// <summary>
///     The unit of work's own phases, in the order Pragmatic registers its interceptors on EF Core (<c>DbContextRegistrationTemplate</c>):
///     auditing, soft delete, ownership, scope, tenant, then the lifecycle events. Each does what its interceptor does, read from it
///     (<c>AuditingInterceptor</c>, <c>SoftDeleteInterceptor</c>, <c>OwnershipInterceptor</c>, <c>ScopeInterceptor</c>, <c>TenantInterceptor</c>,
///     <c>LifecycleEventsInterceptor</c>); the roll-ups move in the batch with the rows they read. The data scope rules a
///     <c>IScopeMaterializationStep</c> applies on EF Core run inside the scope phase, after the creator's scope, as <c>ScopeInterceptor</c> runs
///     its steps (<see cref="MaterializeScopes" />): the neutral contract, over the entries, not EF's save.
/// </summary>
internal static class SavePhases
{
    public static void Apply(IReadOnlyList<ISaveEntry> entries, DateTimeOffset now, string? userId, ITenantContext? tenant, bool requireTenant, CompiledScopeRuleRegistry compiledRules, IReadOnlyList<DataScopeRule>? scopeRules = null)
    {
        Audit(entries, now, userId);
        SoftDelete(entries, now, userId);
        Own(entries, userId);
        Scope(entries, userId);
        MaterializeScopes(entries, compiledRules, scopeRules ?? []);
        Tenant(entries, tenant, requireTenant);
        RaiseLifecycleEvents(entries);
    }

    /// <summary>
    ///     The data scope rules on the rows a save writes (Pragmatic's <c>ScopeMaterializer</c>, which <c>ScopeMaterializationStep</c> runs on EF's
    ///     added and modified entries): each registered rule but a Computed one adds <c>scope:{ScopeName}</c> to a row it matches and removes it
    ///     from one it does not. The predicate is the rule's own, compiled by the engine's generator (<see cref="CompiledScopeRuleRegistry" />, the engine's own).
    /// </summary>
    private static void MaterializeScopes(IReadOnlyList<ISaveEntry> entries, CompiledScopeRuleRegistry compiledRules, IReadOnlyList<DataScopeRule> rules)
    {
        if (rules.Count == 0)
            return;
        foreach (var entry in entries)
        {
            if (!(entry.IsAdded || entry.IsModified) || entry.Entity is not IScopedEntity scoped)
                continue;
            foreach (var rule in rules)
            {
                if (rule.Strategy is ScopeStrategy.Computed || !rule.EntityType.IsInstanceOfType(entry.Entity))
                    continue;
                var scope = ScopeIdentifiers.ForScope(rule.ScopeName);
                if (compiledRules.Matches(rule, entry.Entity))
                {
                    if (!scoped.AccessScopes.Contains(scope))
                        scoped.AccessScopes.Add(scope);
                }
                else
                {
                    scoped.AccessScopes.Remove(scope);
                }
            }
        }
    }

    /// <summary>Added: created and updated now, by the caller when there is one. Modified: updated now. Attribution is never overwritten with null.</summary>
    private static void Audit(IReadOnlyList<ISaveEntry> entries, DateTimeOffset now, string? userId)
    {
        foreach (var entry in entries)
        {
            if (entry.Entity is not IAuditable audited)
                continue;
            if (entry.IsAdded)
            {
                audited.CreatedAt = now;
                audited.UpdatedAt = now;
                if (userId is not null)
                {
                    audited.CreatedBy = userId;
                    audited.UpdatedBy = userId;
                }
            }
            else if (entry.IsModified)
            {
                audited.UpdatedAt = now;
                if (userId is not null)
                    audited.UpdatedBy = userId;
            }
        }
    }

    /// <summary>
    ///     A deleted <see cref="ISoftDelete" /> becomes a modified one, marked now unless already flagged (a cascade's shared instant); a flag raised
    ///     without an instant gets one. Not while <see cref="SoftDeleteScope.IsSuspended" />: an erasure deletes for good.
    /// </summary>
    private static void SoftDelete(IReadOnlyList<ISaveEntry> entries, DateTimeOffset now, string? userId)
    {
        if (SoftDeleteScope.IsSuspended)
            return;
        foreach (var entry in entries)
        {
            if (entry.Entity is not ISoftDelete soft)
                continue;
            if (entry.IsDeleted)
            {
                entry.DeleteAsModified(); // after the audit, as on EF Core: this path gets no UpdatedAt
                if (!soft.IsDeleted)
                    Mark(soft, now, userId);
            }
            else if (entry.IsModified && soft is { IsDeleted: true, DeletedAt: null })
                Mark(soft, now, userId);
        }
    }

    private static void Mark(ISoftDelete soft, DateTimeOffset now, string? userId)
    {
        soft.IsDeleted = true;
        soft.DeletedAt = now;
        soft.DeletedBy = userId;
    }

    /// <summary>An added entity without an owner is the caller's.</summary>
    private static void Own(IReadOnlyList<ISaveEntry> entries, string? userId)
    {
        if (userId is null)
            return;
        foreach (var entry in entries)
        {
            if (entry.IsAdded && entry.Entity is IOwnershipAssignable owned && string.IsNullOrEmpty(owned.OwnerId))
                owned.AssignOwner(userId);
        }
    }

    /// <summary>An added entity without scopes carries the caller's own (<see cref="ScopeIdentifiers.ForUser" />); an update never re-scopes.</summary>
    private static void Scope(IReadOnlyList<ISaveEntry> entries, string? userId)
    {
        if (userId is null)
            return;
        var own = ScopeIdentifiers.ForUser(userId);
        foreach (var entry in entries)
        {
            if (entry.IsAdded && entry.Entity is IScopedEntity { AccessScopes.Count: 0 } scoped)
                scoped.AccessScopes.Add(own);
        }
    }

    /// <summary>
    ///     With a tenant resolved: an added entity is the tenant's whatever it carried, a modified one keeps the tenant it was read with. With none:
    ///     untouched, unless a tenant is required, then the write is refused (<see cref="TenantNotResolvedException" />).
    /// </summary>
    private static void Tenant(IReadOnlyList<ISaveEntry> entries, ITenantContext? tenant, bool requireTenant)
    {
        var tenantId = tenant?.TenantId;
        if (tenant is not { IsResolved: true } || string.IsNullOrEmpty(tenantId))
        {
            if (!requireTenant)
                return;
            foreach (var entry in entries)
            {
                if (entry.Entity is ITenantEntity && (entry.IsAdded || entry.IsModified))
                    throw new TenantNotResolvedException(entry.Entity.GetType().Name);
            }

            return;
        }

        foreach (var entry in entries)
        {
            if (entry.Entity is not ITenantEntity owned)
                continue;
            if (entry.IsAdded)
                owned.TenantId = tenantId;
            else if (entry.IsModified && owned.TenantId != entry.OriginalTenantId)
                owned.TenantId = entry.OriginalTenantId!;
        }
    }

    /// <summary>Created, Updated or Deleted (a soft delete is a delete), asked of the entity; raised only, dispatched after the commit.</summary>
    private static void RaiseLifecycleEvents(IReadOnlyList<ISaveEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Entity is not IRaisesLifecycleEvents raiser)
                continue;
            EntityLifecycle? lifecycle = entry switch
            {
                { IsAdded: true } => EntityLifecycle.Created,
                { IsDeleted: true } => EntityLifecycle.Deleted,
                { IsModified: true } => entry.IsSoftDeleting ? EntityLifecycle.Deleted : EntityLifecycle.Updated,
                _ => null,
            };
            if (lifecycle is { } raised)
                raiser.RaiseLifecycleEvents(raised);
        }
    }
}
