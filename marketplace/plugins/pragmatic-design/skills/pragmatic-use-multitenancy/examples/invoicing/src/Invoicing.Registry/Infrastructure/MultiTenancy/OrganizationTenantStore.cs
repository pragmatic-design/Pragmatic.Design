using Pragmatic.MultiTenancy;

namespace Invoicing.Registry.Infrastructure.MultiTenancy;

/// <summary>
///     The list of tenants that exist, read from the companies this application onboarded.
/// </summary>
/// <remarks>
///     <para>
///         Without this the guards mean nothing. When multi-tenancy is detected the generated host
///         registers an <b>empty</b> <c>InMemoryTenantStore</c> — which is why <c>RequireKnownTenant</c> is
///         off by default: with an empty list, "unknown tenant" would be every tenant. Replacing it with a
///         read view over <c>Organization</c> is what lets the host answer 404 to an id that names nothing
///         and 403 to a company that has been suspended.
///     </para>
///     <para>
///         A read view, and only that. A company is created by <c>OnboardOrganizationAction</c>, which
///         writes the row inside the operation's transaction with its permission checked; the write
///         members below exist because the interface has them, and they refuse rather than opening a
///         second way in that no permission guards.
///     </para>
/// </remarks>
[Service<ITenantStore>]
public sealed class OrganizationTenantStore(IReadRepository<Organization> organizations) : ITenantStore
{
    public async Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
    {
        var organization = await organizations.GetBySlugAsync(tenantId, ct).ConfigureAwait(false);

        return organization is null ? null : Describe(organization);
    }

    public async Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await organizations
            .FindAsync(OrganizationSpecifications.All(), ct)
            .ConfigureAwait(false);

        return [.. all.Select(Describe)];
    }

    /// <summary>The companies a background sweep may work for: the reminder job fans out over these.</summary>
    public async Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct).ConfigureAwait(false);

        return [.. all.Where(tenant => tenant.State == TenantState.Active)];
    }

    public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
        => throw new NotSupportedException(
            "A company becomes a tenant through OnboardOrganizationAction, which checks its permission and " +
            "writes the row in the operation's transaction. Creating one here would be a second way in.");

    public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
        => throw new NotSupportedException(
            "A tenant's state changes through SuspendOrganizationAction and ReactivateOrganizationAction.");

    public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
        => throw new NotSupportedException("Suspending a company is SuspendOrganizationAction.");

    public Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default)
        => throw new NotSupportedException("A company is not deleted: an issued invoice outlives the relationship.");

    private static TenantInfo Describe(Organization organization) => new()
    {
        TenantId = organization.Slug,
        TenantName = organization.LegalName,
        State = organization.State,
        CreatedAt = organization.CreatedAt,
    };
}
