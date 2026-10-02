using Invoicing.Registry;

namespace Invoicing.Billing.Infrastructure.Authorization;

/// <summary>
///     Reads, and writes nothing: the company's own details, its customers and its invoices.
/// </summary>
/// <remarks>
///     The three roles live in Billing because a role grants constants from both boundaries and this is the
///     only assembly that sees both — Registry must not reference Billing, or the dependency that keeps an
///     issued document's truth would become a cycle. Their names are the strings the identity provider sends.
/// </remarks>
[Role("viewer", "Reads the company's invoices and customers")]
[Grants(
    RegistryPermissions.OwnAccess.Read,
    RegistryPermissions.Organization.Read,
    RegistryPermissions.Customer.Read,
    BillingPermissions.Invoice.Read)]
public sealed partial class ViewerRole;
