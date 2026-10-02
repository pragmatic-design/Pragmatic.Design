using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

namespace Pragmatic.Documents.Pdf.Samples.Samples;

/// <summary>
///     Custom page geometry (landscape A4 with narrow margins) plus a hyperlink
///     and a horizontal rule. A common shape for printable tickets, certificates,
///     and labels that need non-default page geometry.
///
///     Note: the BarcodeNode / ImageNode path requires binary resources passed
///     via PdfResources (a dictionary keyed by source path). For a
///     self-contained sample we stay on text + link content — a follow-up
///     sample can add barcode/image rendering once the resource-passing pattern
///     is more broadly documented.
/// </summary>
public static class BarcodeAndLandscapeSample
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

        var bytes = PdfRenderer.Render(model);
        var path = Path.Combine(outputDir, "ticket-landscape.pdf");
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  ticket-landscape.pdf   {bytes.Length} bytes (A4 landscape, narrow margins)");
        Console.WriteLine();
    }
}
