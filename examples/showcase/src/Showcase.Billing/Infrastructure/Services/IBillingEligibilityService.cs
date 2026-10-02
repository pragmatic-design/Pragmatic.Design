namespace Showcase.Billing.Infrastructure.Services;

/// <summary>
/// Checks billing eligibility before a reservation is confirmed.
/// Called on <c>ReservationCreated</c> — before the invoice is actually generated
/// on <c>ReservationConfirmed</c> — to catch billing issues early.
/// </summary>
/// <remarks>
/// In production: credit limit check, fraud detection, blocked guest list, etc.
/// </remarks>
public interface IBillingEligibilityService
{
    /// <summary>
    /// Returns true if the guest is eligible to be billed for the given amount.
    /// A false result should prevent the reservation from being confirmed.
    /// </summary>
    Task<bool> IsEligibleAsync(Guid guestId, decimal amount, string currency, CancellationToken ct = default);
}
