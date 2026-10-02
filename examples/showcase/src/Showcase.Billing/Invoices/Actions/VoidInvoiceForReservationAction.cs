namespace Showcase.Billing.Actions;

/// <summary>
/// Voids the invoice associated with a cancelled reservation.
/// Demonstrates: [DomainAction(Internal = true)] — not exposed via endpoints,
/// invoked only by event handlers (ReservationCancelledHandler).
/// </summary>
[DomainAction(Internal = true)]
[RequirePermission(BillingPermissions.Invoice.Update)]
public partial class VoidInvoiceForReservationAction : VoidDomainAction<NotFoundError>
{
    private IRepository<Invoice> _invoices = null!;

    public required Guid ReservationId { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var spec = Spec<Invoice>.Where(i => i.ReservationId == ReservationId);
        var invoice = await _invoices.FirstOrDefaultAsync(spec, ct).ConfigureAwait(false);

        if (invoice is null)
            return NotFoundError.For("Invoice");

        // Use the generated state-machine transition, which validates the source state and raises any
        // [RaisesEvent] on entry — a raw SetStatus would bypass both. A Paid invoice legitimately
        // cannot be voided this way (no Paid -> Cancelled transition) and returns a ConflictError
        // instead of being silently forced into an invalid state.
        return invoice.TransitionTo(InvoiceStatus.Cancelled);
    }
}
