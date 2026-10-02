using Showcase.Billing.Infrastructure.Services;

namespace Showcase.Billing.Actions;

/// <summary>
/// Refunds a paid invoice via the payment provider and transitions the invoice to Refunded status.
/// Demonstrates:
/// - DomainAction injecting an infrastructure interface (IPaymentOrchestrator — interface in Billing,
///   impl in Host via [Service&lt;IPaymentOrchestrator&gt;]) — same pattern as IBillingEligibilityService
/// - Multi-error result: NotFoundError (implicit via IError) | ConflictError (status guard)
/// - Domain event raised on success (InvoiceRefunded) for cross-boundary notification
/// - [RequirePermission] enforced by PermissionAuthorizationFilter (Order 200)
/// Boundary: BillingBoundary (inferred from namespace).
/// </summary>
[DomainAction]
[RequirePermission(BillingPermissions.Invoice.Refund)]
[ResiliencePolicy("payment-provider")]
[LoadEntity<Invoice>(nameof(Id))]
[Endpoint(HttpVerb.Post, "/api/invoices/{id}/refund")]
[ApiSummary("Refund Invoice")]
[ApiDescription("Refunds a paid invoice, reversing the charge via the payment provider.")]
[ApiTags("Invoices")]
public partial class RefundInvoiceAction : VoidDomainAction<ConflictError>
{
    private IPaymentOrchestrator _paymentOrchestrator = null!;

    /// <summary>The invoice to refund.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Provider-assigned transaction ID of the original charge to reverse.
    /// Required by most payment providers to correlate the refund.
    /// </summary>
    public required string OriginalTransactionId { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        // Initiate refund via payment provider before mutating domain state
        // (fail fast — if provider rejects, invoice stays Paid)
        var refundResult = await _paymentOrchestrator
            .RefundPaymentAsync(
                _invoice.Id,
                OriginalTransactionId,
                _invoice.TotalAmount,
                _invoice.Currency,
                ct)
            .ConfigureAwait(false);

        if (!refundResult.Success)
        {
            return new ConflictError
            {
                EntityType = "Invoice",
                EntityId = _invoice.InvoiceNumber,
                Reason = $"Payment provider rejected refund: {refundResult.ErrorMessage}"
            };
        }

        // Anemic: record the refund reference, then transition; [RaisesEvent<InvoiceRefunded>] on the Refunded
        // state auto-raises the event (TotalAmount/RefundTransactionId filled by name from the entity). The
        // Paid-only guard is the state machine ([TransitionFrom(Paid)] on Refunded).
        _invoice.SetRefundTransactionId(refundResult.TransactionId ?? OriginalTransactionId);
        var domainResult = _invoice.TransitionTo(InvoiceStatus.Refunded);
        if (domainResult.IsFailure)
            return domainResult;

        return Success;
    }
}
