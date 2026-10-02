using Microsoft.Extensions.Logging;

namespace Showcase.Billing.Infrastructure.Services;

/// <summary>
/// Orchestrates payment processing and refunds, selecting the right provider.
/// Implements IPaymentOrchestrator (defined in Showcase.Billing) so BillingActions
/// can inject it without creating a circular reference to Showcase.Host.
/// </summary>
/// <remarks>
/// The provider arrives plain, not keyed: which one a deployment charges through is
/// <see cref="ThePaymentProviderTheHotelUses"/>'s decision, taken from configuration. This class
/// names no provider, so changing provider is not an edit here.
/// </remarks>
[Service<IPaymentOrchestrator>]
[Service(AsSelf = true)]
public class PaymentOrchestrator(
    IPaymentProvider defaultProvider,
    ILogger<PaymentOrchestrator> logger) : IPaymentOrchestrator
{

    public async Task<PaymentResult> ProcessPaymentAsync(
        Guid invoiceId,
        decimal amount,
        string currency,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Processing payment of {Amount} {Currency} via {Provider}",
            amount, currency, defaultProvider.ProviderName);

        return await defaultProvider.ChargeAsync(invoiceId, amount, currency, ct).ConfigureAwait(false);
    }

    public async Task<PaymentResult> RefundPaymentAsync(
        Guid invoiceId,
        string originalTransactionId,
        decimal amount,
        string currency,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Refunding {Amount} {Currency} for Invoice {InvoiceId} via {Provider}",
            amount, currency, invoiceId, defaultProvider.ProviderName);

        return await defaultProvider
            .RefundAsync(invoiceId, originalTransactionId, amount, currency, ct)
            .ConfigureAwait(false);
    }
}
