using Pragmatic.Email.Builder;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     Assertion helpers on InMemoryTransport: the same queries that power
///     test suites. HasSentTo / HasSentWithSubject are common-case one-liners;
///     HasSent / SentWhere take a predicate for arbitrary conditions.
/// </summary>
public static class TestHarnessAssertionsSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Test harness assertion helpers ---");

        var transport = new InMemoryTransport();

        string[] subjects =
        [
            "Reservation confirmed",
            "Shipping update — package in transit",
            "Weekly digest — April",
            "Reservation modified — new check-in date",
        ];
        string[] recipients =
        [
            "alice@example.com",
            "bob@example.com",
            "alice@example.com",
            "alice@example.com",
        ];

        for (var i = 0; i < subjects.Length; i++)
        {
            var message = new EmailMessageBuilder()
                .From("noreply@hotel.com")
                .To(recipients[i])
                .Subject(subjects[i])
                .TextBody($"Body for {subjects[i]}")
                .Build();
            await transport.SendAsync(message);
        }

        var hasAlice = transport.HasSentTo("alice@example.com");
        var hasCarol = transport.HasSentTo("carol@example.com");
        var hasDigest = transport.HasSentWithSubject("Weekly digest — April");
        var hasReservation = transport.HasSent(m => m.Subject.Contains("Reservation"));

        Console.WriteLine($"  total sent                       : {transport.Sent.Count}");
        Console.WriteLine($"  HasSentTo(alice)                 : {hasAlice}");
        Console.WriteLine($"  HasSentTo(carol)                 : {hasCarol}");
        Console.WriteLine($"  HasSentWithSubject(weekly digest): {hasDigest}");
        Console.WriteLine($"  HasSent(reservation predicate)   : {hasReservation}");

        var aliceEmails = transport.SentWhere(m => m.To.Any(a => a.Address == "alice@example.com"));
        Console.WriteLine($"  SentWhere(to=alice)              : {aliceEmails.Count}");
        foreach (var email in aliceEmails)
            Console.WriteLine($"    - {email.Message.Subject}");
        Console.WriteLine();
    }
}
