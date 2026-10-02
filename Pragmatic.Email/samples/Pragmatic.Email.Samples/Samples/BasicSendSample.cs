using Pragmatic.Email.Builder;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     Smallest possible email send: build → send → inspect. The fluent
///     EmailMessageBuilder is the canonical way to construct EmailMessage
///     instances with validation and sensible defaults.
/// </summary>
public static class BasicSendSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Basic send ---");

        var transport = new InMemoryTransport();

        var message = new EmailMessageBuilder()
            .From("noreply@hotel.com", "Hotel Bookings")
            .To("alice@example.com", "Alice")
            .Subject("Your reservation is confirmed")
            .TextBody("Thanks for booking. See you on 2026-05-14.")
            .Build();

        var result = await transport.SendAsync(message);

        Console.WriteLine($"  send succeeded     : {result.Success}  messageId={result.MessageId}");
        Console.WriteLine($"  transport recorded : {transport.Sent.Count} message(s)");
        Console.WriteLine($"  first recipient    : {transport.Sent[0].Message.To[0].Address}");
        Console.WriteLine($"  subject            : {transport.Sent[0].Message.Subject}");
        Console.WriteLine();
    }
}
