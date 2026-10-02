using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

namespace Pragmatic.Documents.Pdf.Samples.Samples;

/// <summary>
///     Structured content: headings, paragraphs, and a table with header + data
///     rows. TableBuilder emits a repeating header on page breaks by default
///     (opt-out via NoRepeatHeader).
/// </summary>
public static class TableAndListSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Tables + structured content ---");

        var model = new DocumentBuilder()
            .Title("Invoice INV-9001")
            .Author("Hotel Billing")
            .Page(p => p
                .Heading("Invoice INV-9001", level: 1)
                .Text("Issued to: Alice Example — alice@example.com")
                .Spacer()
                .Heading("Line items", level: 2)
                .Table(t => t
                    .Column(width: 60)
                    .Column()
                    .Column(width: 25, align: TextAlign.Right)
                    .Column(width: 30, align: TextAlign.Right)
                    .HeaderRow("SKU", "Description", "Qty", "Amount")
                    .Row("HTL-NT",   "Standard room, 4 nights",    "4", "€ 520.00")
                    .Row("HTL-BK",   "Breakfast (daily)",          "4", "€  60.00")
                    .Row("SPA-MSG",  "Massage (60 min)",           "1", "€  85.00")
                    .Row("TAX-VAT",  "VAT 10%",                    "",  "€  66.50"))
                .Spacer()
                .HorizontalRule()
                .Text("Total due: € 731.50")
                .Spacer()
                .Heading("Payment terms", level: 2)
                .Text(
                    "Payment due within 14 days. Bank details and IBAN are printed on the " +
                    "next page. Thank you for staying with us."))
            .Build();

        var bytes = PdfRenderer.Render(model);
        var path = Path.Combine(outputDir, "invoice.pdf");
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  invoice.pdf            {bytes.Length} bytes");
        Console.WriteLine();
    }
}
