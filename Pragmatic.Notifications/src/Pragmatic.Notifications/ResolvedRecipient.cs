using Pragmatic.Notifications.Preferences;

namespace Pragmatic.Notifications;

/// <summary>
///     A recipient resolved to a concrete delivery address and channel.
/// </summary>
public sealed record ResolvedRecipient(
    string Address,
    NotificationChannel Channel,
    string? Locale,
    string? TenantId,
    NotificationPreferences? Preferences);
