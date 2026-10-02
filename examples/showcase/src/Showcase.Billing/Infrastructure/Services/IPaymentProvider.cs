namespace Showcase.Billing.Infrastructure.Services;

/// <summary>
/// Processes payments and refunds for invoices.
/// </summary>
public interface IPaymentProvider
{
    string ProviderName { get; }

    Task<PaymentResult> ChargeAsync(
        Guid invoiceId,
        decimal amount,
        string currency,
        CancellationToken ct = default);

    Task<PaymentResult> RefundAsync(
        Guid invoiceId,
        string originalTransactionId,
        decimal amount,
        string currency,
        CancellationToken ct = default);
}
