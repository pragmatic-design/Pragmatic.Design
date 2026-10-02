namespace Showcase.Billing.Infrastructure.Services;

/// <summary>
/// Orchestrates payment operations (charge + refund) across providers.
/// Implemented by PaymentOrchestrator in Showcase.Host; injected into Billing domain actions.
/// Same pattern as IBillingEligibilityService — interface in Billing, implementation in Host.
/// </summary>
public interface IPaymentOrchestrator
{
    Task<PaymentResult> ProcessPaymentAsync(
        Guid invoiceId,
        decimal amount,
        string currency,
        CancellationToken ct = default);

    Task<PaymentResult> RefundPaymentAsync(
        Guid invoiceId,
        string originalTransactionId,
        decimal amount,
        string currency,
        CancellationToken ct = default);
}
