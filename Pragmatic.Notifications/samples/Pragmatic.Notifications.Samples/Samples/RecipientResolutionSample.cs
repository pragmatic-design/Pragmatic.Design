using Pragmatic.Notifications;
using Pragmatic.Notifications.Testing;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     NotificationRecipient supports four targeting modes: single user, user
///     set, role (all users in a role), tenant (all tenant admins). Plus direct
///     email/phone/webhook URLs for bypass scenarios. Each factory method on
///     the NotificationRecipient type enforces a consistent shape.
/// </summary>
public static class RecipientResolutionSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Recipient resolution modes ---");

        var harness = new NotificationTestHarness();

        var content = new NotificationContent
        {
            Subject = "Planned maintenance window",
            Body = "Service restart scheduled for Sunday 02:00 UTC.",
        };

        // Single user.
        await harness.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.User("user-42"),
            Content = content,
        });

        // Multiple users in one batch.
        await harness.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Users(["user-1", "user-2", "user-3"]),
            Content = content,
        });

        // Everyone in a role.
        await harness.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.Admin,
            Recipient = NotificationRecipient.Role("support-agents"),
            Content = content,
        });

        // Tenant admins.
        await harness.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.Admin,
            Recipient = NotificationRecipient.Tenant("acme"),
            Content = content,
        });

        // Direct email bypass (e.g. newsletter, no user lookup).
        await harness.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = new NotificationRecipient { EmailAddress = "press@example.com" },
            Content = content,
        });

        Console.WriteLine($"  total sent         : {harness.Sent.Count}");
        Console.WriteLine($"  single-user sends  : {harness.SentWhere(r => r.Recipient.UserId is not null).Count}");
        Console.WriteLine($"  multi-user sends   : {harness.SentWhere(r => r.Recipient.UserIds is not null).Count}");
        Console.WriteLine($"  role sends         : {harness.SentWhere(r => r.Recipient.RoleName is not null).Count}");
        Console.WriteLine($"  tenant sends       : {harness.SentWhere(r => r.Recipient.TenantId is not null).Count}");
        Console.WriteLine($"  direct-email sends : {harness.SentWhere(r => r.Recipient.EmailAddress is not null).Count}");
        Console.WriteLine();
    }
}
