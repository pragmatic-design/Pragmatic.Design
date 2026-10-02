using Invoicing.Registry;

namespace Invoicing.Billing.Infrastructure.Authorization;

/// <summary>
///     Keeps the invoices: drafts and changes them, issues them, downloads the documents, records what was
///     paid. Everything a viewer may do, and more.
/// </summary>
/// <remarks>
///     Creating, correcting and removing customers is this role's too: whoever bills keeps the register of
///     who is billed, and undoing one's own mistake is not a privilege of its own — the restore asks for
///     the same permission as an update.
/// </remarks>
[Role("accountant", "Drafts, issues and collects the invoices")]
[IncludesRole<ViewerRole>]
[Grants(
    RegistryPermissions.Customer.Create,
    RegistryPermissions.Customer.Update,
    RegistryPermissions.Customer.Delete,
    BillingPermissions.Invoice.Create,
    BillingPermissions.Invoice.Update,
    BillingPermissions.Invoice.Delete,
    BillingPermissions.Invoice.Issue,
    BillingPermissions.Invoice.Void,
    BillingPermissions.Invoice.Download,
    BillingPermissions.Payment.Record)]
public sealed partial class AccountantRole;
