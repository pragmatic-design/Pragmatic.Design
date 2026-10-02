using Pragmatic.Notifications;
using Pragmatic.Notifications.Testing;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     SendAsync runs through the pipeline synchronously and returns a terminal
///     result. EnqueueAsync hands off to a background processor so the request
///     path returns immediately; the terminal delivery result surfaces later
///     via tracking. The harness records the Synchronous flag so tests can
///     assert the chosen path.
/// </summary>
public static class EnqueuedVsSyncSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Send vs Enqueue ---");

        var harness = new NotificationTestHarness();

        var content = new NotificationContent
        {
            Subject = "Password reset link",
            Body = "Click to reset your password.",
        };

        // Synchronous — for time-sensitive flows where the caller needs the
        // delivery result before returning to the user.
        await harness.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.User("user-42"),
            Content = content,
            Priority = NotificationPriority.High,
        });

        // Enqueued — for bulk or non-blocking flows. The caller gets an id
        // immediately; delivery happens in the background.
        await harness.EnqueueAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Role("newsletter-subscribers"),
            Content = new() { Subject = "Newsletter — April", Body = "..." },
            Priority = NotificationPriority.Low,
            Category = "marketing",
        });

        foreach (var s in harness.Sent)
        {
            var mode = s.Synchronous ? "SYNC " : "QUEUE";
            Console.WriteLine(
                $"  [{mode}] {s.Request.Content.Subject,-30}  at={s.SentAt:HH:mm:ss.fff}");
        }
        Console.WriteLine();
    }
}
