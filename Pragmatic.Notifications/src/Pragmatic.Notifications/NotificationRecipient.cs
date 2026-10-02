namespace Pragmatic.Notifications;

/// <summary>
///     Identifies notification recipients. Supports user, role, tenant, direct address, and webhook targets.
/// </summary>
public sealed record NotificationRecipient
{
    /// <summary>Single user by ID.</summary>
    public string? UserId { get; init; }

    /// <summary>Multiple users by ID.</summary>
    public IReadOnlyList<string>? UserIds { get; init; }

    /// <summary>All users in a role.</summary>
    public string? RoleName { get; init; }

    /// <summary>All admins of a tenant.</summary>
    public string? TenantId { get; init; }

    /// <summary>Direct email address (bypasses user resolution).</summary>
    public string? EmailAddress { get; init; }

    /// <summary>Direct phone number (bypasses user resolution).</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>Direct webhook URL.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>Creates a recipient targeting a single user.</summary>
    public static NotificationRecipient User(string userId) => new() { UserId = userId };

    /// <summary>Creates a recipient targeting multiple users.</summary>
    public static NotificationRecipient Users(IReadOnlyList<string> userIds) => new() { UserIds = userIds };

    /// <summary>Creates a recipient targeting all users in a role.</summary>
    public static NotificationRecipient Role(string roleName) => new() { RoleName = roleName };

    /// <summary>Creates a recipient targeting tenant admins.</summary>
    public static NotificationRecipient Tenant(string tenantId) => new() { TenantId = tenantId };

    /// <summary>Creates a recipient targeting a direct email.</summary>
    public static NotificationRecipient Direct(string emailAddress) => new() { EmailAddress = emailAddress };

    /// <summary>Creates a recipient targeting a webhook URL.</summary>
    public static NotificationRecipient ToWebhook(string webhookUrl) => new() { WebhookUrl = webhookUrl };
}
