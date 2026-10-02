namespace Invoicing.Registry.Organizations.Queries;

/// <summary>
///     The issuer's own details, read by the other module when it makes a document.
/// </summary>
/// <remarks>
///     The slug is a parameter rather than the resolved tenant, because <c>Organization</c> is the one
///     entity no tenant filter applies to — it is what a tenant id names. The caller passes the tenant it
///     is already running as.
/// </remarks>
[Query<Organization, OrganizationBillingDetailsDto>(Single = true)]
[Published]
public partial class GetOrganizationBillingDetailsQuery
{
    [Filter]
    public string Slug { get; init; } = "";
}
