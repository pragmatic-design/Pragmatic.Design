using Pragmatic.Notifications;
using Pragmatic.Notifications.Testing;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     Audience decides who the notification is for (EndUser / Staff / System)
///     and drives the resolver strategy. Priority influences channel routing:
///     Critical notifications fan out across every available channel, Low may
///     be suppressed by user quiet-hours preferences.
/// </summary>
public static class AudienceAndPrioritySample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Audience and priority ---");

        var harness = new NotificationTestHarness();

        // Routine end-user transactional message.
        await harness.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.User("user-42"),
            Content = new() { Subject = "Invoice INV-9001 is ready", Body = "..." },
            Priority = NotificationPriority.Normal,
            Category = "transactional",
        });

        // Marketing blast to a segment, low priority.
        await harness.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Role("newsletter-subscribers"),
            Content = new() { Subject = "Spring specials inside", Body = "..." },
            Priority = NotificationPriority.Low,
            Category = "marketing",
        });

        // Ops alert for on-call engineers, critical.
        await harness.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.Admin,
            Recipient = NotificationRecipient.Role("oncall"),
            Content = new() { Subject = "DB CPU > 90% for 5m", Body = "..." },
            Priority = NotificationPriority.Critical,
            Category = "ops-alert",
        });

        // System event to internal consumers.
        await harness.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.System,
            Recipient = new NotificationRecipient { WebhookUrl = "https://hooks.internal/events" },
            Content = new() { Subject = "user.signed_up", Body = "user-99" },
            Priority = NotificationPriority.High,
            Category = "system-event",
        });

        foreach (var s in harness.Sent)
        {
            Console.WriteLine(
                $"  {s.Request.Audience,-7}  {s.Request.Priority,-8}  [{s.Request.Category}]  \"{s.Request.Content.Subject}\"");
        }
        Console.WriteLine();
    }
}
