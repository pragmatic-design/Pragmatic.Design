namespace Pragmatic.Notifications;

/// <summary>
///     Notification priority. Affects channel routing and delivery strategy.
/// </summary>
public enum NotificationPriority
{
    /// <summary>Digest-eligible, batched delivery.</summary>
    Low = 0,

    /// <summary>Standard delivery via preferred channel.</summary>
    Normal = 1,

    /// <summary>Delivered via push + email.</summary>
    High = 2,

    /// <summary>Delivered via all enabled channels immediately.</summary>
    Critical = 3,
}
