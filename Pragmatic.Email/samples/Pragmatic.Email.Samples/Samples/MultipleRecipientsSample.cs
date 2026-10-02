using Pragmatic.Email.Builder;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     Multiple To/Cc/Bcc recipients plus ReplyTo. Builder method chaining
///     keeps the common case readable even with 5+ recipients across the
///     three classes.
/// </summary>
public static class MultipleRecipientsSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Multiple recipients (To / Cc / Bcc / ReplyTo) ---");

        var transport = new InMemoryTransport();

        var message = new EmailMessageBuilder()
            .From("ops@hotel.com", "Ops Team")
            .To("guest1@example.com", "Primary Guest")
            .To("guest2@example.com", "Secondary Guest")
            .Cc("accounting@hotel.com")
            .Cc("manager@hotel.com", "Hotel Manager")
            .Bcc("audit@hotel.com")
            .ReplyTo("support@hotel.com", "Support")
            .Subject("Group booking confirmation — party of 2")
            .TextBody("Your group booking is confirmed. Welcome.")
            .Build();

        await transport.SendAsync(message);

        var recorded = transport.Sent[0].Message;
        Console.WriteLine($"  To  : {string.Join(", ", recorded.To.Select(a => a.Address))}");
        Console.WriteLine($"  Cc  : {string.Join(", ", recorded.Cc?.Select(a => a.Address) ?? [])}");
        Console.WriteLine($"  Bcc : {string.Join(", ", recorded.Bcc?.Select(a => a.Address) ?? [])}");
        Console.WriteLine($"  ReplyTo : {recorded.ReplyTo?.Address}");
        Console.WriteLine();
    }
}
