namespace Pragmatic.Notifications;

/// <summary>
///     Target audience for a notification. Determines recipient resolution strategy.
/// </summary>
public enum NotificationAudience
{
    /// <summary>Application end-user (customer, guest, subscriber).</summary>
    EndUser = 0,

    /// <summary>Application administrator (ops, platform admin).</summary>
    Admin = 1,

    /// <summary>Developer or DevOps (CI/CD alerts, error reports).</summary>
    Developer = 2,

    /// <summary>Tenant-specific administrator in multi-tenant apps.</summary>
    TenantAdmin = 3,

    /// <summary>System-level (monitoring, health checks, webhooks).</summary>
    System = 4,
}
