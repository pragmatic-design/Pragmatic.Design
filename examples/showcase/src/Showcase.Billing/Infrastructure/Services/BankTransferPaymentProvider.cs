using Microsoft.Extensions.Logging;

namespace Showcase.Billing.Infrastructure.Services;

/// <summary>
/// Bank transfer payment provider (simulated).
/// Demonstrates: [Service(Key = "bank")] — second keyed implementation.
/// </summary>
#pragma warning disable PRAG1646 // Keyed services — net10.0 target supports .NET 8+
[Service<IPaymentProvider>(Key = "bank")]
#pragma warning restore PRAG1646
public class BankTransferPaymentProvider(ILogger<BankTransferPaymentProvider> logger) : IPaymentProvider
{
    public string ProviderName => "BankTransfer";

    public Task<PaymentResult> ChargeAsync(
        Guid invoiceId,
        decimal amount,
        string currency,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "BankTransfer: processing {Amount} {Currency} for Invoice {InvoiceId}",
            amount, currency, invoiceId);

        // Simulated success
        return Task.FromResult(new PaymentResult
        {
            Success = true,
            TransactionId = $"bank_{Guid.NewGuid():N}"
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
            "BankTransfer: refunding {Amount} {Currency} for Invoice {InvoiceId} (original: {TxId})",
            amount, currency, invoiceId, originalTransactionId);

        // Simulated success
        return Task.FromResult(new PaymentResult
        {
            Success = true,
            TransactionId = $"bank_refund_{Guid.NewGuid():N}"
        });
    }
}
