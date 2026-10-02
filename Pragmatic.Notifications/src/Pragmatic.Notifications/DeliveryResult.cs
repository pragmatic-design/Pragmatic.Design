namespace Pragmatic.Notifications;

/// <summary>
///     Result of delivering a notification to a single recipient via a single channel.
/// </summary>
public sealed record DeliveryResult(bool Success, string? ProviderId, string? ErrorMessage)
{
    public static DeliveryResult Succeeded(string? providerId = null) => new(true, providerId, null);
    public static DeliveryResult Failed(string errorMessage) => new(false, null, errorMessage);
}
