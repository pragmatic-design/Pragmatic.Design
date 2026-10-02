using Microsoft.Extensions.Logging;

namespace Showcase.Billing.Infrastructure.Services;

/// <summary>
/// Demo implementation — always approves.
/// In production: query a credit service, check fraud rules, validate payment methods.
/// </summary>
[Service<IBillingEligibilityService>]
public sealed partial class BillingEligibilityService(ILogger<BillingEligibilityService> logger)
    : IBillingEligibilityService
{
    public Task<bool> IsEligibleAsync(Guid guestId, decimal amount, string currency, CancellationToken ct = default)
    {
        // In production: call credit provider API, check guest payment history, etc.
        LogEligibilityChecked(guestId, amount, currency);
        return Task.FromResult(true);
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Billing eligibility check passed for guest {GuestId}: {Amount} {Currency}")]
    private partial void LogEligibilityChecked(Guid guestId, decimal amount, string currency);
}
