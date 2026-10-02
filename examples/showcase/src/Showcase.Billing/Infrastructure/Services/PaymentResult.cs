namespace Showcase.Billing.Infrastructure.Services;

/// <summary>
/// Result of a payment operation.
/// </summary>
public sealed record PaymentResult
{
    public bool Success { get; init; }
    public string? TransactionId { get; init; }
    public string? ErrorMessage { get; init; }
}
