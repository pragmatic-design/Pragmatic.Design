using Pragmatic.Documents.Docx;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Samples.Samples;

/// <summary>
///     Landscape A4 with narrow margins + a hyperlink. Same page-geometry
///     shape as the PDF landscape sample, so the two outputs are directly
///     comparable.
/// </summary>
public static class LandscapeAndHyperlinkSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Landscape A4 + narrow margins + hyperlink ---");

        var model = new DocumentBuilder()
            .Title("Loyalty ticket — ALICE-42")
            .Author("Hotel Loyalty Program")
            .Size(PageSize.A4)
            .Landscape()
            .WithMargins(Margins.Narrow)
            .Page(p => p
                .Heading("Welcome, Alice", level: 1)
                .Text(
                    "This voucher entitles the holder to one complimentary breakfast " +
                    "at any participating property. Present the code at reception.")
                .Spacer(20)
                .Heading("Voucher code: ALICE-42", level: 2)
                .Text("Valid until: 2026-12-31")
                .Spacer(10)
                .Hyperlink("https://hotel.example.com/redeem?token=ALICE-42",
                    "Redeem online")
                .Spacer()
                .HorizontalRule()
                .Text(
                    "Terms and conditions apply. Not transferable. See the hotel website " +
                    "for the full policy."))
            .Build();

        var bytes = DocxRenderer.Render(model);
        var path = Path.Combine(outputDir, "ticket-landscape.docx");
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  ticket-landscape.docx  {bytes.Length} bytes (A4 landscape, narrow margins)");
        Console.WriteLine();
    }
}
