using Pragmatic.Notifications;
using Pragmatic.Notifications.Testing;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     Simplest possible send: construct a NotificationRequest, call SendAsync
///     on the test harness, inspect the recorded result. The same request shape
///     is what production INotificationService.SendAsync accepts.
/// </summary>
public static class BasicSendSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Basic send ---");

        var harness = new NotificationTestHarness();

        var result = await harness.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.User("user-42"),
            Content = new NotificationContent
            {
                Subject = "Welcome!",
                Body = "Thanks for signing up.",
                HtmlBody = "<h1>Welcome!</h1><p>Thanks for signing up.</p>",
            },
        });

        Console.WriteLine($"  send result        : {(result.Success ? "OK" : "FAIL")}  id={result.NotificationId}");
        Console.WriteLine($"  harness recorded   : {harness.Sent.Count} send(s)");
        Console.WriteLine($"  sent to user-42?   : {harness.HasSent(r => r.Recipient.UserId == "user-42")}");
        Console.WriteLine($"  subject contains?  : {harness.HasSentWithSubject("Welcome!")}");
        Console.WriteLine();
    }
}
