using System.Text;
using Pragmatic.Email.Builder;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     File attachments plus inline images. Attach() adds a downloadable file;
///     InlineImage() adds a content-id-referenced image that renders inside
///     the HTML body via `cid:` URLs — no external hosting needed.
/// </summary>
public static class AttachmentsSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Attachments + inline images ---");

        var transport = new InMemoryTransport();

        // Fake invoice PDF bytes (real apps would render via Pragmatic.Documents.Pdf).
        var invoicePdf = Encoding.UTF8.GetBytes("%PDF-1.4\n%fake-invoice-body\n");
        // 1x1 transparent PNG.
        var logoPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgAAIAAAUAAeImBZsAAAAASUVORK5CYII=");

        var message = new EmailMessageBuilder()
            .From("billing@hotel.com", "Hotel Billing")
            .To("alice@example.com")
            .Subject("Invoice INV-9001")
            .HtmlBody("""
                <html>
                  <body>
                    <img src="cid:logo" alt="Hotel logo" />
                    <h2>Invoice INV-9001</h2>
                    <p>Invoice PDF attached. Thank you for your stay.</p>
                  </body>
                </html>
                """)
            .InlineImage("logo", logoPng, contentType: "image/png")
            .Attach("invoice-INV-9001.pdf", invoicePdf, contentType: "application/pdf")
            .Build();

        await transport.SendAsync(message);

        var recorded = transport.Sent[0].Message;
        Console.WriteLine($"  attachments      : {recorded.Attachments?.Count}");
        foreach (var att in recorded.Attachments ?? [])
        {
            var kind = att.IsInline ? "inline " : "file   ";
            Console.WriteLine($"    {kind} {att.FileName,-26} {att.ContentType,-20} {att.Data.Length} bytes  cid={att.ContentId}");
        }
        Console.WriteLine();
    }
}
