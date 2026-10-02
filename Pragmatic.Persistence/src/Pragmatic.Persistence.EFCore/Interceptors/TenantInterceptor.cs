using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Persistence.EFCore.Interceptors;

/// <summary>
///     EF Core interceptor that automatically sets <see cref="ITenantEntity.TenantId" />
///     on newly created entities from the current <see cref="ITenantContext" />.
/// </summary>
/// <remarks>
///     <para>
///         When a tenant is resolved (a request scope), this is secure-by-default: a new
///         <see cref="ITenantEntity" /> has its <see cref="ITenantEntity.TenantId" /> OVERWRITTEN with
///         the current tenant — a caller cannot persist a row for another tenant by pre-setting the
///         value — and a modification that changes TenantId (a tenant transfer) is reverted.
///     </para>
///     <para>
///         When no tenant is resolved (<see cref="ITenantContext.IsResolved" /> is false — a background
///         job, seed, or CLI), entities are left untouched: that path legitimately assigns TenantId
///         explicitly and runs outside any single-tenant request.
///     </para>
///     <para>
///         ⚠️ <b>Unless <c>MultiTenancyOptions.RequireTenant</c> is on, in which case such a
///         write is refused.</b> The query filter is fail-closed on reads, and the write path matches it:
///         a consumer is all writes, and a message that arrived with no tenant restored into its scope
///         would otherwise be written onto the <b>shared</b> database with an empty tenant, invisible
///         afterwards to the service's own reads. The option promises both halves — "an unresolved
///         tenant causes failure (and tenant queries return no rows…)".
///     </para>
/// </remarks>
/// <param name="tenantContext">The ambient tenant, or none.</param>
/// <param name="requireTenant">
///     Whether a write of an <see cref="ITenantEntity" /> with no resolved tenant must be refused —
///     <c>MultiTenancyOptions.RequireTenant</c>, as the generated registration reads it.
/// </param>
/// <remarks>
///     ⚠️ A <see cref="bool" /> and not the options type, because this assembly cannot name it: the
///     options live in <c>Pragmatic.MultiTenancy</c>, which references this package, and the reverse
///     would be a cycle. The marker interfaces are in <c>Pragmatic.Abstractions</c>, which is why they
///     are usable here and the options are not. The host's generated registration resolves
///     <c>IOptions&lt;MultiTenancyOptions&gt;</c> — it can — and passes the answer.
/// </remarks>
public sealed class TenantInterceptor(
    ITenantContext tenantContext,
    bool requireTenant = false) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyTenantId(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyTenantId(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyTenantId(DbContext? context)
    {
        if (context is null)
            return;

        var tenantId = tenantContext.TenantId;

        if (!tenantContext.IsResolved || string.IsNullOrEmpty(tenantId))
        {
            RefuseIfATenantWasRequired(context);
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<ITenantEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                // Overwrite, not stamp-if-empty: a caller must not be able to persist a row for another
                // tenant by pre-setting TenantId.
                entry.Entity.TenantId = tenantId;
            }
            else if (entry.State == EntityState.Modified)
            {
                // Reject tenant transfer on update — revert any change to TenantId to its loaded value.
                var tenantProperty = entry.Property(e => e.TenantId);
                if (tenantProperty.IsModified)
                    tenantProperty.CurrentValue = tenantProperty.OriginalValue;
            }
        }
    }

    /// <summary>
    ///     Refuses a write of an <see cref="ITenantEntity" /> when no tenant is resolved and the
    ///     application said one is required — the same stance the read filter takes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Every tracked tenant entity, not only the ones with an empty TenantId.</b> A row
    ///         carrying a tenant id somebody set by hand is exactly what a caller must not be able to
    ///         write from outside that tenant's scope — it is the same escalation the overwrite above
    ///         exists to close, arriving by the path where there is no tenant to overwrite with.
    ///     </para>
    ///     <para>
    ///         ⚠️ The parameter defaults to <see langword="false" />, so an interceptor built by hand
    ///         has no guard unless the caller that built it asks for one. Everything
    ///         the framework wires passes the application's own setting, so a deployment is covered.
    ///     </para>
    /// </remarks>
    private void RefuseIfATenantWasRequired(DbContext context)
    {
        if (!requireTenant)
            return;

        foreach (var entry in context.ChangeTracker.Entries<ITenantEntity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                throw new TenantNotResolvedException(entry.Metadata.ClrType.Name);
        }
    }
}
