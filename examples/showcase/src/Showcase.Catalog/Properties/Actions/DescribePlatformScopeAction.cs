namespace Showcase.Catalog.Properties.Actions;

/// <summary>
/// Says which tenant a platform operation runs in: none.
///
/// Demonstrates: <c>[NoTenant]</c>. A platform operation (onboarding, an operator's cross-tenant report) must not run inside a tenant
/// the caller brings, where its reads would be filtered to that tenant and its writes stamped with it. The tenant middleware refuses a
/// request that resolves one with 400 <c>TENANT_NOT_ALLOWED</c>, and serves one that does not with no tenant, although the host requires
/// a tenant everywhere else. <c>[TenantAgnostic]</c> would serve the caller in the tenant it brought.
/// </summary>
[DomainAction]
[Endpoint(HttpVerb.Get, "api/properties/platform-scope")]
[ApiSummary("Describe Platform Scope")]
[ApiTags("Properties")]
[NoTenant]
public partial class DescribePlatformScopeAction : DomainAction<string>
{
    private ITenantContext _tenant = null!;

    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<string, IError>>(_tenant.TenantId ?? "no tenant");
}
