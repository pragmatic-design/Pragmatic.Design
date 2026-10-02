using Pragmatic.Documents.Email;
using Pragmatic.Email.Model;

namespace Pragmatic.Documents.Email.Samples.Samples;

/// <summary>
///     Compliance-oriented footer: social bar + unsubscribe footer with the
///     fine print required by most jurisdictions (sender address, opt-out link).
///     A realistic newsletter always ends with this block.
/// </summary>
public static class FooterAndSocialSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Footer + social bar ---");

        var model = new EmailBuilder()
            .Subject("April newsletter — Hotel Milano")
            .Preheader("Community highlights, new arrivals, spring schedule")
            .Width(600)
            .Section(s => s.Column(c => c
                .Heading("April highlights", level: 1)
                .Text("A quick roundup of what's new at Hotel Milano this month.")
                .Spacer()
                .Text("• New chef joins the hotel restaurant")
                .Text("• Garden area reopens for the season")
                .Text("• Weekend spa packages now bookable online")))
            .SocialBar(
            [
                new SocialLink("https://cdn.example.com/icons/twitter.png",   "https://twitter.com/hotelmilano",   "Twitter"),
                new SocialLink("https://cdn.example.com/icons/instagram.png", "https://instagram.com/hotelmilano", "Instagram"),
                new SocialLink("https://cdn.example.com/icons/facebook.png",  "https://facebook.com/hotelmilano",  "Facebook"),
            ],
            backgroundColor: "#F4F5F7")
            .Footer(c => c
                .Text("Hotel Milano — Via Roma 42, 20121 Milano, Italy",
                    EmailTextAlign.Center, color: "#6B6F76")
                .Text("You received this because you booked a stay with us.",
                    EmailTextAlign.Center, color: "#6B6F76")
                .Text("Prefer fewer emails? Unsubscribe at https://hotel.example.com/unsubscribe",
                    EmailTextAlign.Center, color: "#6B6F76"))
            .Build();

        var renderer = new EmailHtmlRenderer();
        var html = renderer.Render(model);
        var path = Path.Combine(outputDir, "footer-social.html");
        File.WriteAllText(path, html);
        Console.WriteLine($"  footer-social.html     {html.Length} chars (SocialBar + Footer)");
        Console.WriteLine();
    }
}
