using Pragmatic.Notifications;
using Pragmatic.Notifications.Testing;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     Category is a free-form string that drives preference-based routing
///     (users can mute "marketing" without muting "transactional"). The harness
///     exposes query helpers that make category-based test assertions one-liners.
/// </summary>
public static class CategoryAndTrackingSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Category filtering + harness queries ---");

        var harness = new NotificationTestHarness();

        string[] transactionalSubjects =
        [
            "Order confirmed",
            "Shipping update",
            "Invoice available",
        ];
        string[] marketingSubjects =
        [
            "Spring sale — 20% off",
            "New features just landed",
        ];

        foreach (var subject in transactionalSubjects)
        {
            await harness.SendAsync(new NotificationRequest
            {
                Audience = NotificationAudience.EndUser,
                Recipient = NotificationRecipient.User("user-42"),
                Content = new() { Subject = subject, Body = "..." },
                Category = "transactional",
            });
        }

        foreach (var subject in marketingSubjects)
        {
            await harness.SendAsync(new NotificationRequest
            {
                Audience = NotificationAudience.EndUser,
                Recipient = NotificationRecipient.User("user-42"),
                Content = new() { Subject = subject, Body = "..." },
                Category = "marketing",
            });
        }

        var transactional = harness.SentWhere(r => r.Category == "transactional");
        var marketing = harness.SentWhere(r => r.Category == "marketing");

        Console.WriteLine($"  total sent             : {harness.Sent.Count}");
        Console.WriteLine($"  transactional          : {transactional.Count}");
        foreach (var n in transactional)
            Console.WriteLine($"    - {n.Request.Content.Subject}");
        Console.WriteLine($"  marketing              : {marketing.Count}");
        foreach (var n in marketing)
            Console.WriteLine($"    - {n.Request.Content.Subject}");

        // Classic test-style assertion helpers.
        Console.WriteLine($"  any shipping update?   : {harness.HasSentWithSubject("Shipping update")}");
        Console.WriteLine($"  any critical priority? : {harness.HasSent(r => r.Priority == NotificationPriority.Critical)}");
        Console.WriteLine();
    }
}
