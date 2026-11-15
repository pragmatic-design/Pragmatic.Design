namespace Pragmatic.Notifications.Pipeline;

/// <summary>
///     Resolves a <see cref="NotificationRecipient"/> into concrete delivery addresses per channel.
/// </summary>
public interface IRecipientResolver
{
    /// <summary>
    ///     Resolves the recipient to one or more concrete addresses with channel and preference information, and names the recipients it
    ///     could not reach (<see cref="RecipientResolution.Unreached" />), which the notification's result reports.
    /// </summary>
    Task<RecipientResolution> ResolveAsync(
        NotificationRecipient recipient,
        NotificationAudience audience,
        CancellationToken ct = default);
}
