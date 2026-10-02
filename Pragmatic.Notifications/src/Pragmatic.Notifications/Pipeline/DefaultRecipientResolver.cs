using Microsoft.Extensions.Logging;
using Pragmatic.Notifications.Preferences;

namespace Pragmatic.Notifications.Pipeline;

/// <summary>
///     Default recipient resolver. Maps direct addresses to resolved recipients.
///     For user ID resolution, a custom <see cref="IRecipientResolver"/> should be registered.
/// </summary>
internal sealed partial class DefaultRecipientResolver(
    INotificationPreferenceProvider preferenceProvider,
    ILogger<DefaultRecipientResolver> logger)
    : IRecipientResolver
{
    public async Task<IReadOnlyList<ResolvedRecipient>> ResolveAsync(
        NotificationRecipient recipient,
        NotificationAudience audience,
        CancellationToken ct = default)
    {
        var results = new List<ResolvedRecipient>();

        // Direct email
        if (recipient.EmailAddress is not null)
        {
            var prefs = await preferenceProvider.GetPreferencesAsync(recipient.EmailAddress, ct).ConfigureAwait(false);
            results.Add(new ResolvedRecipient(recipient.EmailAddress, NotificationChannel.Email, prefs?.Locale, null, prefs));
        }

        // Direct webhook
        if (recipient.WebhookUrl is not null)
        {
            var prefs = await preferenceProvider.GetPreferencesAsync(recipient.WebhookUrl, ct).ConfigureAwait(false);
            results.Add(new ResolvedRecipient(recipient.WebhookUrl, NotificationChannel.Webhook, prefs?.Locale, null, prefs));
        }

        // Direct phone. Preferences are loaded for every channel, not just email: an opt-out or a muted
        // category applies to the recipient, and leaving them null here let SMS ignore do-not-disturb.
        if (recipient.PhoneNumber is not null)
        {
            var prefs = await preferenceProvider.GetPreferencesAsync(recipient.PhoneNumber, ct).ConfigureAwait(false);
            results.Add(new ResolvedRecipient(recipient.PhoneNumber, NotificationChannel.Sms, prefs?.Locale, null, prefs));
        }

        // User ID → requires a custom IRecipientResolver for real email/address lookup.
        // DefaultRecipientResolver cannot resolve a user ID to a delivery address; skipping.
        if (recipient.UserId is not null)
        {
            LogUserIdNotResolved(recipient.UserId);
        }

        // Multiple user IDs → same limitation; requires custom resolver.
        if (recipient.UserIds is { Count: > 0 })
        {
            foreach (var userId in recipient.UserIds)
                LogUserIdNotResolved(userId);
        }

        // Role → requires Identity integration (default: no-op)
        if (recipient.RoleName is not null)
            LogRecipientTypeNotSupported("Role", recipient.RoleName);

        // TenantId → requires MultiTenancy integration (default: no-op)
        if (recipient.TenantId is not null)
            LogRecipientTypeNotSupported("TenantId", recipient.TenantId);

        return results;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "DefaultRecipientResolver cannot resolve UserId '{UserId}' to a delivery address. Register a custom IRecipientResolver for user ID lookup.")]
    private partial void LogUserIdNotResolved(string userId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "DefaultRecipientResolver does not support {RecipientType} recipients (value: '{Value}'). Register a custom IRecipientResolver for {RecipientType} resolution.")]
    private partial void LogRecipientTypeNotSupported(string recipientType, string value);
}
