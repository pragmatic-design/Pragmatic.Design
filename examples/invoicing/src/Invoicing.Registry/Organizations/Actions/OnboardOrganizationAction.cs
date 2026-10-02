using Invoicing.Registry.Errors;

namespace Invoicing.Registry.Organizations.Actions;

/// <summary>
///     Takes a company into the service: it becomes a tenant, and from then on everything its people
///     write belongs to it.
/// </summary>
/// <remarks>
///     <c>[TenantAgnostic]</c> is what makes this reachable at all. The tenant middleware runs at order 92,
///     before the route, and refuses a request that resolves no tenant — and the company being onboarded
///     does not exist yet, so its administrator's token cannot carry it. <c>[AllowAnonymous]</c> would not
///     help: the refusal is about the tenant, not the identity, and this operation is certainly not
///     anonymous.
/// </remarks>
[DomainAction]
[RequirePermission(RegistryPermissions.Organization.Onboard)]
[TenantAgnostic]
[Endpoint(HttpVerb.Post, "api/organizations/onboard")]
public partial class OnboardOrganizationAction : DomainAction<Guid, OrganizationSlugTakenError>
{
    private IRepository<Organization> _organizations = null!;
    private IReadRepository<Organization> _existing = null!;

    /// <summary>The tenant id to come: lowercase, and what the provider will put in the token.</summary>
    [Required]
    [MaxLength(40)]
    public required string Slug { get; init; }

    [Required]
    [MaxLength(200)]
    public required string LegalName { get; init; }

    [Required]
    [MaxLength(10)]
    public required string InvoiceNumberPrefix { get; init; }

    [MaxLength(20)]
    public string VatNumber { get; init; } = "";

    [Email]
    [MaxLength(320)]
    public string SenderEmail { get; init; } = "";

    public string AddressStreet { get; init; } = "";

    public string AddressPostCode { get; init; } = "";

    public string AddressCity { get; init; } = "";

    [MaxLength(2)]
    public string AddressCountry { get; init; } = "";

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        // Lowercase here rather than in a rule: the slug is compared with a claim, and a difference in
        // case would be a tenant that exists and is never found.
        var slug = Slug.ToLowerInvariant();

        if (await _existing.GetBySlugAsync(slug, ct).ConfigureAwait(false) is not null)
            return new OrganizationSlugTakenError();

        var organization = Organization.Create();
        organization.SetSlug(slug);
        organization.SetLegalName(LegalName);
        organization.SetInvoiceNumberPrefix(InvoiceNumberPrefix);
        organization.SetVatNumber(VatNumber);
        organization.SetSenderEmail(SenderEmail);
        organization.SetAddress(new PostalAddress(AddressStreet, AddressPostCode, AddressCity, AddressCountry));

        _organizations.Add(organization);

        return organization.PersistenceId;
    }
}
