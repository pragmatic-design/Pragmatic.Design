using Pragmatic.Email.Builder;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     Multipart/alternative: provide both a plaintext and an HTML body. Mail
///     clients pick the richest format they can render; accessibility tools
///     and plain-text-only recipients fall back to TextBody.
/// </summary>
public static class HtmlAndTextBodySample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Multipart body (text + HTML) ---");

        var transport = new InMemoryTransport();

        var message = new EmailMessageBuilder()
            .From("noreply@hotel.com", "Hotel Bookings")
            .To("alice@example.com")
            .Subject("Your May reservation")
            .TextBody(
                "Your reservation is confirmed.\n" +
                "Check-in: 2026-05-14  —  Check-out: 2026-05-18\n" +
                "Visit https://hotel.com/reservations/INV-9001 for details.")
            .HtmlBody("""
                <html>
                  <body style="font-family: sans-serif">
                    <h2>Your reservation is confirmed</h2>
                    <p><strong>Check-in:</strong> 2026-05-14<br/>
                       <strong>Check-out:</strong> 2026-05-18</p>
                    <p><a href="https://hotel.com/reservations/INV-9001">View reservation details</a></p>
                  </body>
                </html>
                """)
            .Build();

        await transport.SendAsync(message);

        var recorded = transport.Sent[0].Message;
        Console.WriteLine($"  text body length : {recorded.TextBody?.Length}");
        Console.WriteLine($"  html body length : {recorded.HtmlBody?.Length}");
        Console.WriteLine($"  is multipart?    : {recorded.TextBody is not null && recorded.HtmlBody is not null}");
        Console.WriteLine();
    }
}
