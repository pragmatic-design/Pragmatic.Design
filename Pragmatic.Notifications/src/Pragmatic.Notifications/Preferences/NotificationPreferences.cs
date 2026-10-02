namespace Pragmatic.Notifications.Preferences;

/// <summary>
///     User-level notification preferences.
/// </summary>
public sealed record NotificationPreferences
{
    /// <summary>Whether notifications are enabled for this user.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Preferred delivery channel (when priority allows choice).</summary>
    public NotificationChannel PreferredChannel { get; init; } = NotificationChannel.Email;

    /// <summary>Categories the user has muted.</summary>
    public IReadOnlySet<string>? MutedCategories { get; init; }

    /// <summary>Do-not-disturb: if set, non-critical notifications are held.</summary>
    public bool DoNotDisturb { get; init; }

    /// <summary>User locale for localized content (e.g., "it-IT", "en-US").</summary>
    public string? Locale { get; init; }
}
