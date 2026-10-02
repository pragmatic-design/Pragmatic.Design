using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Documents.Templates;
using Pragmatic.Imaging;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Documents.Pdf.Tests;

/// <summary>
/// Generates real PDF files for manual quality inspection.
/// Output: tests/output/*.pdf
/// </summary>
public class PdfOutputTests
{
    private static readonly string OutputDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "output");

    private static void SavePdf(byte[] pdf, string fileName)
    {
        Directory.CreateDirectory(OutputDir);
        var path = Path.Combine(OutputDir, fileName);
        File.WriteAllBytes(path, pdf);
        Console.WriteLine($"PDF saved: {Path.GetFullPath(path)} ({pdf.Length:N0} bytes)");
    }

    /// <summary>Helper: generate barcode resource name matching Rust sanitize_resource_name</summary>
    private static string SanitizeResourceName(string value)
        => new(value.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray());

    // --- Test 1: Invoice from C# builder with QR code ---

    [NativeRequiredFact]
    public void Output_Invoice_WithQrCode()
    {
        var paymentUrl = "https://pay.pragmatic.design/invoice/2026-042";

        // Generate QR code PNG via Pragmatic.Imaging
        var qrPng = QrCode.GeneratePng(paymentUrl, moduleSize: 8, margin: 1);

        // Build document with barcode node referencing the payment URL
        var model = new DocumentBuilder()
            .Title("Fattura #2026-042")
            .Author("Pragmatic S.r.l.")
            .Language("it-IT")
            .WithMargins(new Margins { Top = 25, Right = 20, Bottom = 25, Left = 20 })
            .Page(p => p
                .Header(new TextNode { Content = "PRAGMATIC S.R.L. — Via Roma 42, 20121 Milano" })
                .Heading("FATTURA N. 2026-042", level: 1)
                .Spacer(3)
                .Text("Data: 09 Aprile 2026")
                .Text("Cliente: Mario Rossi — P.IVA IT98765432101")
                .Spacer(5)
                .HorizontalRule()
                .Spacer(3)
                .Table(t => t
                    .Column(width: 90)
                    .Column(width: 20, align: TextAlign.Center)
                    .Column(width: 25, align: TextAlign.Right)
                    .Column(width: 25, align: TextAlign.Right)
                    .HeaderRow("Descrizione", "Qtà", "Prezzo", "Totale")
                    .Row("Licenza Pragmatic Enterprise", "1", "€4.000", "€4.000")
                    .Row("Supporto Premium Annuale", "1", "€1.200", "€1.200")
                    .Row("Formazione Team (2gg)", "1", "€800", "€800")
                )
                .Spacer(3)
                .HorizontalRule()
                .Text("Subtotale: €6.000,00")
                .Text("IVA 22%: €1.320,00")
                .Heading("TOTALE: €7.320,00", level: 3)
                .Spacer(5)
                .Text("IBAN: IT60 X054 2811 1010 0000 0123 456")
                .Spacer(5)
                .Barcode(paymentUrl, BarcodeType.QrCode)
                .Text("Scansiona per pagare online")
                .Footer(
                    new TextNode { Content = "Documento fiscale ai sensi dell'art. 21 DPR 633/72" })
            )
            .Build();

        // Pass QR code as resource
        var resourceKey = SanitizeResourceName(paymentUrl);
        var resources = new PdfResources { [resourceKey] = qrPng };

        var pdf = PdfRenderer.Render(model, resources);

        pdf.Should().NotBeEmpty();
        pdf.Length.Should().BeGreaterThan(5000);
        SavePdf(pdf, "invoice-qr.pdf");
    }

    // --- Test 2: PDX-Doc markup → resolve → PDF (full E2E) ---

    [NativeRequiredFact]
    public async Task Output_Invoice_FromPdxMarkup_E2E()
    {
        var paymentUrl = "https://pay.pragmatic.design/invoice/2026-042";
        var qrPng = QrCode.GeneratePng(paymentUrl, moduleSize: 8, margin: 1);

        var markup = """
            <document title="Fattura #{{invoice.number}}" author="{{company.name}}" lang="it-IT"
                      page-size="A4" margin="25 20 25 20">
                <page>
                    <header>
                        <text>{{company.name}} — {{company.address}}</text>
                    </header>

                    <heading level="1">FATTURA N. {{invoice.number}}</heading>
                    <spacer height="3" />
                    <text>Data: {{invoice.date | date:"dd MMMM yyyy"}}</text>
                    <text>Cliente: {{customer.name}} — P.IVA {{customer.vatId}}</text>
                    <spacer height="3" />
                    <hr />

                    <table data-source="invoice.items">
                        <column width="90">Descrizione</column>
                        <column width="20" align="center">Qtà</column>
                        <column width="25" align="right">Prezzo</column>
                        <column width="25" align="right">Totale</column>
                        <row-template>
                            <cell>{{item.description}}</cell>
                            <cell>{{item.qty}}</cell>
                            <cell>{{item.price | currency:"EUR"}}</cell>
                            <cell>{{item.total | currency:"EUR"}}</cell>
                        </row-template>
                    </table>

                    <spacer height="3" />
                    <hr />
                    <text>Subtotale: {{invoice.subtotal | currency:"EUR"}}</text>
                    <text>IVA 22%: {{invoice.vat | currency:"EUR"}}</text>
                    <heading level="3">TOTALE: {{invoice.total | currency:"EUR"}}</heading>
                    <spacer height="3" />
                    <text>IBAN: {{company.iban}}</text>
                    <spacer height="3" />
                    <barcode value="{{invoice.paymentUrl}}" type="qr" width="25" height="25" />
                    <text>Scansiona per pagare online</text>

                    <footer>
                        <text>{{t:invoice.legal}}</text>
                    </footer>
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);

        var localizer = new TestLocalizer("it-IT", new Dictionary<string, string>
        {
            ["invoice.legal"] = "Documento fiscale emesso ai sensi dell'art. 21 DPR 633/72."
        });

        var pipes = PipeRegistry.Default.WithI18N();
        var resolver = new DocumentTemplateResolver(pipes);
        var ctx = new TemplateDataContext()
            .WithCulture("it-IT")
            .WithLocalizer(localizer)
            .AddSource("company", new Dictionary<string, object?>
            {
                ["name"] = "PRAGMATIC S.R.L.",
                ["address"] = "Via Roma 42, 20121 Milano",
                ["iban"] = "IT60 X054 2811 1010 0000 0123 456"
            })
            .AddSource("customer", new Dictionary<string, object?>
            {
                ["name"] = "Mario Rossi",
                ["vatId"] = "IT98765432101"
            })
            .AddSource("invoice", new Dictionary<string, object?>
            {
                ["number"] = "2026-042",
                ["date"] = new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.FromHours(2)),
                ["subtotal"] = 6000.0,
                ["vat"] = 1320.0,
                ["total"] = 7320.0,
                ["paymentUrl"] = paymentUrl,
                ["items"] = new List<Dictionary<string, object?>>
                {
                    new() { ["description"] = "Licenza Pragmatic Enterprise", ["qty"] = "1", ["price"] = 4000.0, ["total"] = 4000.0 },
                    new() { ["description"] = "Supporto Premium Annuale", ["qty"] = "1", ["price"] = 1200.0, ["total"] = 1200.0 },
                    new() { ["description"] = "Formazione Team (2gg)", ["qty"] = "1", ["price"] = 800.0, ["total"] = 800.0 }
                }
            });

        var model = await resolver.ResolveAsync(template, ctx);

        // Generate QR resource using the resolved barcode value
        var barcodeNode = model.Pages[0].Content.OfType<BarcodeNode>().FirstOrDefault();
        var pdfResources = new PdfResources();
        if (barcodeNode is not null)
        {
            var qr = QrCode.GeneratePng(barcodeNode.Value, moduleSize: 8, margin: 1);
            pdfResources[SanitizeResourceName(barcodeNode.Value)] = qr;
        }

        var pdf = PdfRenderer.Render(model, pdfResources);

        pdf.Should().NotBeEmpty();
        SavePdf(pdf, "invoice-pdx-e2e.pdf");
    }

    // --- Test 3: Batch payslips ---

    [NativeRequiredFact]
    public async Task Output_BatchPayslips()
    {
        var markup = """
            <document title="Cedolini Aprile 2026" page-data-source="employees" page-item="emp"
                      page-size="A4" margin="25 20 25 20">
                <page>
                    <header><text>PRAGMATIC S.R.L. — Cedolino Paga Aprile 2026</text></header>

                    <heading level="2">{{emp.name}}</heading>
                    <text>Matricola: {{emp.id}} — Reparto: {{emp.department}} — {{emp.role}}</text>
                    <spacer height="5" />

                    <table data-source="emp.voci">
                        <column width="100">Voce</column>
                        <column width="30" align="right">Competenze</column>
                        <column width="30" align="right">Trattenute</column>
                        <row-template>
                            <cell>{{item.voce}}</cell>
                            <cell>{{item.competenze | currency:"EUR"}}</cell>
                            <cell>{{item.trattenute | currency:"EUR"}}</cell>
                        </row-template>
                    </table>
                    <spacer height="3" />
                    <hr />
                    <text>Lordo: {{emp.gross | currency:"EUR"}}</text>
                    <text>Trattenute: {{emp.deductions | currency:"EUR"}}</text>
                    <heading level="3">NETTO IN BUSTA: {{emp.net | currency:"EUR"}}</heading>

                    <footer><text>Documento riservato</text></footer>
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);
        var pipes = PipeRegistry.Default.WithI18N();
        var resolver = new DocumentTemplateResolver(pipes);

        var ctx = new TemplateDataContext()
            .WithCulture("it-IT")
            .AddSource("employees", new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["name"] = "Alice Bianchi", ["id"] = "EMP-001",
                    ["department"] = "Engineering", ["role"] = "Senior Developer",
                    ["gross"] = 3500.0, ["deductions"] = 1050.0, ["net"] = 2450.0,
                    ["voci"] = new List<Dictionary<string, object?>>
                    {
                        new() { ["voce"] = "Retribuzione base", ["competenze"] = 2800.0, ["trattenute"] = 0.0 },
                        new() { ["voce"] = "Superminimo", ["competenze"] = 500.0, ["trattenute"] = 0.0 },
                        new() { ["voce"] = "Buoni pasto", ["competenze"] = 200.0, ["trattenute"] = 0.0 },
                        new() { ["voce"] = "IRPEF", ["competenze"] = 0.0, ["trattenute"] = 750.0 },
                        new() { ["voce"] = "INPS", ["competenze"] = 0.0, ["trattenute"] = 300.0 },
                    }
                },
                new()
                {
                    ["name"] = "Bob Verdi", ["id"] = "EMP-002",
                    ["department"] = "Sales", ["role"] = "Account Manager",
                    ["gross"] = 3200.0, ["deductions"] = 960.0, ["net"] = 2240.0,
                    ["voci"] = new List<Dictionary<string, object?>>
                    {
                        new() { ["voce"] = "Retribuzione base", ["competenze"] = 2500.0, ["trattenute"] = 0.0 },
                        new() { ["voce"] = "Provvigioni", ["competenze"] = 700.0, ["trattenute"] = 0.0 },
                        new() { ["voce"] = "IRPEF", ["competenze"] = 0.0, ["trattenute"] = 680.0 },
                        new() { ["voce"] = "INPS", ["competenze"] = 0.0, ["trattenute"] = 280.0 },
                    }
                }
            });

        var model = await resolver.ResolveAsync(template, ctx);
        model.Pages.Should().HaveCount(2);

        var pdf = PdfRenderer.Render(model);
        pdf.Should().NotBeEmpty();
        SavePdf(pdf, "payslips-batch.pdf");
    }

    private sealed class TestLocalizer(string culture, Dictionary<string, string> t) : IStringLocalizer
    {
        public string Culture { get; } = culture;
        public TranslationResult this[string key] => t.TryGetValue(key, out var v) ? TranslationResult.Found(key, v) : TranslationResult.Missing(key);
        public TranslationResult this[string key, params object[] args] => t.TryGetValue(key, out var v) ? TranslationResult.Found(key, string.Format(v, args)) : TranslationResult.Missing(key);
        public TranslationResult Plural(string key, int count) => this[key];
        public TranslationResult Plural(string key, int count, params object[] args) => this[key, args];
        public IStringLocalizer WithCulture(string culture) => this;
    }
}
