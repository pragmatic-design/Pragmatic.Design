using Pragmatic.Documents.Email;
using Pragmatic.Email.Model;

namespace Pragmatic.Documents.Email.Samples.Samples;

/// <summary>
///     Minimum viable email: subject + preheader + one section with a heading,
///     paragraph, and call-to-action button. Rendered through
///     EmailHtmlRenderer to a string suitable for an SMTP HtmlBody or an
///     integration with Pragmatic.Email / Pragmatic.Notifications.
/// </summary>
public static class SimpleEmailSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Simple transactional email ---");

        var model = new EmailBuilder()
            .Subject("Your reservation is confirmed")
            .Preheader("Check-in: 14 May 2026 — Hotel Milano")
            .Language("en")
            .Section(s => s.Column(c => c
                .Heading("Reservation confirmed", level: 1)
                .Text("Hi Alice, thanks for booking Hotel Milano.")
                .Text("We look forward to welcoming you on 14 May 2026.")
                .Spacer()
                .Button("View reservation", "https://hotel.example.com/reservations/INV-9001",
                    backgroundColor: "#0A66C2")))
            .Build();

        var renderer = new EmailHtmlRenderer();
        var html = renderer.Render(model);
        var path = Path.Combine(outputDir, "simple.html");
        File.WriteAllText(path, html);
        Console.WriteLine($"  simple.html            {html.Length} chars");
        Console.WriteLine();
    }
}
