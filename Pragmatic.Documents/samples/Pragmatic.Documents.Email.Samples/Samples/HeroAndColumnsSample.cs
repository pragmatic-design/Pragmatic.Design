using Pragmatic.Documents.Email;
using Pragmatic.Email.Model;

namespace Pragmatic.Documents.Email.Samples.Samples;

/// <summary>
///     Marketing-style layout: a Hero block at the top (image + heading +
///     body + CTA) followed by a TwoColumns section. Demonstrates the common
///     "attention grabber + detail grid" pattern shipped by most newsletter
///     and promotional email templates.
/// </summary>
public static class HeroAndColumnsSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Hero + two-column layout ---");

        var model = new EmailBuilder()
            .Subject("Spring specials — up to 20% off")
            .Preheader("Limited-time pricing on suites and packages")
            .BackgroundColor("#F4F5F7")
            .Width(600)
            .Hero(h => h
                .Image("https://cdn.example.com/hero-spring.jpg", "Sunny hotel lobby", width: 600)
                .Heading("Spring specials are here", level: 1)
                .Text("Enjoy 20% off on suites and spa packages through May.")
                .Button("Book now", "https://hotel.example.com/spring", backgroundColor: "#0A66C2"),
                backgroundColor: "#ffffff")
            .TwoColumns(
                left: c => c
                    .Heading("Family suites", level: 2)
                    .Text("Connecting rooms, breakfast included, kids stay free.")
                    .Button("Explore family suites", "https://hotel.example.com/family"),
                right: c => c
                    .Heading("Spa retreats", level: 2)
                    .Text("Weekend spa packages starting at €280 per night.")
                    .Button("View spa packages", "https://hotel.example.com/spa"))
            .Build();

        var renderer = new EmailHtmlRenderer();
        var html = renderer.Render(model);
        var path = Path.Combine(outputDir, "hero-columns.html");
        File.WriteAllText(path, html);
        Console.WriteLine($"  hero-columns.html      {html.Length} chars (Hero + TwoColumns)");
        Console.WriteLine();
    }
}
