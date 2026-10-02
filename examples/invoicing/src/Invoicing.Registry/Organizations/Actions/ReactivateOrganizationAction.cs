namespace Invoicing.Registry.Organizations.Actions;

/// <summary>
///     Serves a suspended company again.
/// </summary>
/// <remarks>
///     The same permission as suspending — whoever may stop a company may start it again, and splitting
///     the two would leave the power to suspend without the power to undo it. <c>[TenantAgnostic]</c>
///     because a suspended tenant is exactly the one the state guard refuses: reactivating from inside it
///     could never work.
/// </remarks>
[DomainAction]
[RequirePermission(RegistryPermissions.Organization.Suspend)]
[TenantAgnostic]
[Endpoint(HttpVerb.Post, "api/organizations/{slug}/reactivate")]
public partial class ReactivateOrganizationAction : DomainAction<bool, NotFoundError>
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

        organization.Reactivate();
        _organizations.Update(organization);

        return true;
    }
}
