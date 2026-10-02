namespace Invoicing.Billing.Invoices.Queries;

/// <summary>One invoice, by id — this company's, because the tenant filter is not optional.</summary>
/// <remarks>
///     The route the create's <c>[CreatedAt("/api/invoices/{Id}")]</c> already promised. Without it a
///     draft answered <b>201</b> with a <c>Location</c> that answered <b>405</b>, which the generated
///     tenant-isolation contract found the moment it started being emitted for this create:
///     a caller in another company got Method Not Allowed where it had to get Not Found, and no test
///     could tell the two apart before.
/// </remarks>
[Query<Invoice, InvoiceDto>(Single = true)]
[RequirePermission(BillingPermissions.Invoice.Read)]
[Endpoint(HttpVerb.Get, "api/invoices/{id}")]
public partial class GetInvoiceQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
