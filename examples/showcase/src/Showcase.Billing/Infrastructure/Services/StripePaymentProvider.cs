using Microsoft.Extensions.Logging;

namespace Showcase.Billing.Infrastructure.Services;

/// <summary>
/// Stripe payment provider (simulated).
/// Demonstrates: [Service(Key = "stripe")] — keyed service for named resolution.
/// </summary>
#pragma warning disable PRAG1646 // Keyed services — net10.0 target supports .NET 8+
[Service<IPaymentProvider>(Key = "stripe")]
#pragma warning restore PRAG1646
public class StripePaymentProvider(ILogger<StripePaymentProvider> logger) : IPaymentProvider
{
    public string ProviderName => "Stripe";

    public Task<PaymentResult> ChargeAsync(
        Guid invoiceId,
        decimal amount,
        string currency,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Stripe: charging {Amount} {Currency} for Invoice {InvoiceId}",
            amount, currency, invoiceId);

        // Simulated success
        return Task.FromResult(new PaymentResult
        {
            Success = true,
            TransactionId = $"stripe_{Guid.NewGuid():N}"
        });
    }

    public Task<PaymentResult> RefundAsync(
        Guid invoiceId,
        string originalTransactionId,
        decimal amount,
        string currency,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Stripe: refunding {Amount} {Currency} for Invoice {InvoiceId} (original: {TxId})",
            amount, currency, invoiceId, originalTransactionId);

        // Simulated success
        return Task.FromResult(new PaymentResult
        {
            Success = true,
            TransactionId = $"stripe_refund_{Guid.NewGuid():N}"
        });
    }
}
