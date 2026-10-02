namespace Showcase.Billing.Actions;

/// <summary>
/// Marks an invoice as paid.
/// Demonstrates: [Endpoint] on DomainAction — VoidDomainAction whose state move is declared with
/// [TransitionsTo] and performed by the invoker on the [LoadEntity] row, before the body.
/// Also demonstrates: [FromClaim] binding — PaidByUserId is extracted from the JWT "sub" claim.
/// </summary>
[DomainAction]
[RequirePermission(BillingPermissions.Invoice.Update)]
[LoadEntity<Invoice>(nameof(Id))]
[Endpoint(HttpVerb.Post, "/api/invoices/{id}/pay")]
[TransitionsTo<InvoiceStatus>(InvoiceStatus.Paid)]
[ApiSummary("Mark Invoice Paid")]
[ApiTags("Invoices")]
public partial class MarkInvoicePaidAction : VoidDomainAction<NotFoundError>
{
    public required Guid Id { get; init; }

    /// <summary>The user who authorized the payment — optionally bound from JWT "sub" claim.</summary>
    [FromClaim("sub", IsRequired = false)]
    public Guid PaidByUserId { get; set; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        // The move to Paid is [TransitionsTo], performed by the invoker before this body: it validates the
        // Issued/Draft -> Paid move and answers 409 otherwise, and [RaisesEvent<InvoicePaid>] on the target
        // raises the event (ctor filled from the entity's members). What is left here is the rest of the fact.
        _invoice.SetPaidByUserId(PaidByUserId);

        return Success;
    }
}
