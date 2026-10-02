namespace Invoicing.Billing.Payments.Queries;

/// <summary>
///     What has been paid against one invoice, in the order it arrived.
/// </summary>
/// <remarks>
///     Read by whoever may read the invoice: the payments are part of what an invoice's page shows, and a
///     separate permission for them would say that a viewer can see what is charged but not what has
///     arrived — which is not a distinction this business makes.
/// </remarks>
[Query<Payment, PaymentDto>]
[RequirePermission(BillingPermissions.Invoice.Read)]
[Endpoint(HttpVerb.Get, "api/invoices/{id}/payments")]
public partial class ListInvoicePaymentsQuery
{
    /// <summary>The invoice, from the route. Not optional: this read is always of one invoice.</summary>
    [Filter(MapTo = nameof(Payment.InvoiceId))]
    public Guid Id { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? PaidOnSort { get; init; }
}
