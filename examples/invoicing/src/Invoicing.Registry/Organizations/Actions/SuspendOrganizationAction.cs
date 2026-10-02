namespace Invoicing.Registry.Organizations.Actions;

/// <summary>
///     Stops serving a company: its people's tokens are refused from the next request, and not one row of
///     its data is touched.
/// </summary>
/// <remarks>
///     <c>[TenantAgnostic]</c> for the same reason as onboarding, and one more: suspending a tenant from
///     inside that same tenant would be refused by the state guard as soon as it succeeded once.
/// </remarks>
[DomainAction]
[RequirePermission(RegistryPermissions.Organization.Suspend)]
[TenantAgnostic]
[Endpoint(HttpVerb.Post, "api/organizations/{slug}/suspend")]
public partial class SuspendOrganizationAction : DomainAction<bool, NotFoundError>
{
    private IRepository<Organization> _organizations = null!;

    [Required]
    [MaxLength(40)]
    public required string Slug { get; init; }

    public override async Task<Result<bool, IError>> Execute(CancellationToken ct = default)
    {
        var organization = await _organizations
            .GetBySlugAsync(Slug.ToLowerInvariant(), ct)
            .ConfigureAwait(false);
        if (organization is null)
            return NotFoundError.For(nameof(Organization), Slug);

        organization.Suspend();
        _organizations.Update(organization);

        return true;
    }
}
