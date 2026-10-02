using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Docx;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Documents.Templates;

namespace Pragmatic.Documents.E2E.Tests;

/// <summary>
/// Full E2E pipeline: PDX Markup → Parse → Template → Resolve → DocumentModel → DOCX Render.
/// </summary>
public class DocxPipelineTests
{
    private static readonly string OutputDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "output");

    private static void SaveDocx(byte[] docx, string fileName)
    {
        Directory.CreateDirectory(OutputDir);
        File.WriteAllBytes(Path.Combine(OutputDir, fileName), docx);
    }

    [Fact]
    public async Task PdxMarkup_Invoice_ToDocx_FullPipeline()
    {
        // 1. PDX markup
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

                    <footer>
                        <text>{{t:invoice.legal}}</text>
                    </footer>
                </page>
            </document>
            """;

        // 2. Parse PDX → Template
        var template = PdxDocParser.Parse(markup);

        // 3. Data context
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
                ["address"] = "Via Roma 42, 20121 Milano"
            })
            .AddSource("customer", new Dictionary<string, object?>
            {
                ["name"] = "Mario Rossi",
                ["vatId"] = "IT98765432101"
            })
            .AddSource("invoice", new Dictionary<string, object?>
            {
                ["number"] = "2026-042",
                ["date"] = new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.FromHours(2)),
                ["subtotal"] = 6000.0,
                ["vat"] = 1320.0,
                ["total"] = 7320.0,
                ["items"] = new List<Dictionary<string, object?>>
                {
                    new() { ["description"] = "Licenza Pragmatic Enterprise", ["qty"] = "1", ["price"] = 4000.0, ["total"] = 4000.0 },
                    new() { ["description"] = "Supporto Premium Annuale", ["qty"] = "1", ["price"] = 1200.0, ["total"] = 1200.0 },
                    new() { ["description"] = "Formazione Team (2gg)", ["qty"] = "1", ["price"] = 800.0, ["total"] = 800.0 }
                }
            });

        // 4. Resolve template → DocumentModel
        var model = await resolver.ResolveAsync(template, ctx);

        model.Should().NotBeNull();
        model.Title.Should().Contain("2026-042");
        model.Pages.Should().HaveCount(1);

        // 5. Render DocumentModel → DOCX
        var docx = DocxRenderer.Render(model);

        // 6. Verify
        docx.Should().NotBeEmpty();
        docx[0].Should().Be(0x50); // PK
        docx[1].Should().Be(0x4B);
        docx.Length.Should().BeGreaterThan(2000);

        SaveDocx(docx, "e2e-invoice-pdx.docx");
    }

    [Fact]
    public async Task PdxMarkup_BatchPayslips_ToDocx()
    {
        var markup = """
            <document title="Cedolini Aprile 2026" page-data-source="employees" page-item="emp"
                      page-size="A4" margin="25 20 25 20">
                <page>
                    <header><text>PRAGMATIC S.R.L. — Cedolino Paga Aprile 2026</text></header>
                    <heading level="2">{{emp.name}}</heading>
                    <text>Matricola: {{emp.id}} — Reparto: {{emp.department}}</text>
                    <spacer height="3" />
                    <table data-source="emp.voci">
                        <column width="100">Voce</column>
                        <column width="30" align="right">Importo</column>
                        <row-template>
                            <cell>{{item.voce}}</cell>
                            <cell>{{item.importo | currency:"EUR"}}</cell>
                        </row-template>
                    </table>
                    <hr />
                    <heading level="3">NETTO: {{emp.net | currency:"EUR"}}</heading>
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
                    ["name"] = "Alice Bianchi", ["id"] = "EMP-001", ["department"] = "Engineering", ["net"] = 2450.0,
                    ["voci"] = new List<Dictionary<string, object?>>
                    {
                        new() { ["voce"] = "Retribuzione base", ["importo"] = 2800.0 },
                        new() { ["voce"] = "Superminimo", ["importo"] = 500.0 },
                        new() { ["voce"] = "IRPEF", ["importo"] = -750.0 },
                    }
                },
                new()
                {
                    ["name"] = "Bob Verdi", ["id"] = "EMP-002", ["department"] = "Sales", ["net"] = 2240.0,
                    ["voci"] = new List<Dictionary<string, object?>>
                    {
                        new() { ["voce"] = "Retribuzione base", ["importo"] = 2500.0 },
                        new() { ["voce"] = "Provvigioni", ["importo"] = 700.0 },
                        new() { ["voce"] = "IRPEF", ["importo"] = -680.0 },
                    }
                }
            });

        var model = await resolver.ResolveAsync(template, ctx);
        model.Pages.Should().HaveCount(2);

        var docx = DocxRenderer.Render(model);

        docx.Should().NotBeEmpty();
        docx[0].Should().Be(0x50);

        SaveDocx(docx, "e2e-payslips-pdx.docx");
    }
}
