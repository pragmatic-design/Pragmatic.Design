namespace Pragmatic.Notifications.Pipeline;

/// <summary>
///     Resolves a <see cref="NotificationRecipient"/> into concrete delivery addresses per channel.
/// </summary>
public interface IRecipientResolver
{
    /// <summary>
    ///     Resolves the recipient to one or more concrete addresses with channel and preference information.
    /// </summary>
    Task<IReadOnlyList<ResolvedRecipient>> ResolveAsync(
        NotificationRecipient recipient,
        NotificationAudience audience,
        CancellationToken ct = default);
}
