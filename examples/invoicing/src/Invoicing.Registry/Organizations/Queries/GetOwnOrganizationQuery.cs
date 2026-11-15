using Pragmatic.MultiTenancy;

namespace Invoicing.Registry.Organizations.Queries;

/// <summary>
///     The company the caller is signed in as: its own details, for the header a client shows.
/// </summary>
/// <remarks>
///     <para>
///         <c>Organization</c> is the one entity no tenant filter applies to — it is what a tenant id names —
///         so the slug has to be named. <c>[FromTenant]</c> names it from the resolved tenant: the invoker
///         writes it, and a <c>slug</c> the caller puts in the query string is not read, so nobody reads
///         another company's details by asking for them.
///     </para>
///     <para>
///         The same permission as <c>api/me</c>, which every role holds: it is the caller's own company.
///     </para>
/// </remarks>
[Query<Organization, OrganizationBillingDetailsDto>(Single = true)]
[RequirePermission(RegistryPermissions.OwnAccess.Read)]
[Endpoint(HttpVerb.Get, "api/me/company")]
public partial class GetOwnOrganizationQuery
{
    /// <summary>The tenant the request resolved, which is the company's slug.</summary>
    [FromTenant]
    public string Slug { get; private set; } = "";

    public Specification<Organization> TheCallersCompany => Spec<Organization>.Where(o => o.Slug == Slug);
}
