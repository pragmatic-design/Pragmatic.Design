using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Xlsx.Tests;

/// <summary>
/// Output tests that generate XLSX files for visual verification in Excel/LibreOffice.
/// Files are written to tests/output/ directory.
/// </summary>
public class XlsxOutputTests
{
    private static readonly string OutputDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "output");

    [Fact]
    public void Output_FullReport()
    {
        var model = new SpreadsheetBuilder()
            .Title("Report Q1 2026")
            .Author("Pragmatic")
            .Sheet("Riepilogo", s => s
                .Column(width: 25).Column(width: 15).Column(width: 15).Column(width: 15)
                .FreezeRows(1)
                .Merge("A1", "D1")
                .HeaderRow("Prodotto", "Q1", "Q2", "Variazione")
                .Row("Enterprise", 1050000, 1200000)
                .Row("Professional", 590000, 680000)
                .Row("Starter", 120000, 145000)
                .FormulaRow("Totale", "=SUM(B2:B4)", "=SUM(C2:C4)", "=C5-B5")
            )
            .Sheet("Dettaglio", s => s
                .Column(width: 20).Column(width: 12).Column(width: 12).Column(width: 15)
                .HeaderRow("Cliente", "Prodotto", "Licenze", "Revenue")
                .Row("Acme Corp", "Enterprise", 50, 525000)
                .Row("Globex", "Enterprise", 50, 525000)
                .Row("Wayne Ent.", "Professional", 100, 590000)
                .StyledRow(
                    new Cell { Value = "TOTALE", Style = new CellStyle { Bold = true } },
                    new Cell { Value = "" },
                    new Cell { Value = "", Style = new CellStyle { Bold = true } },
                    new Cell { Value = 1640000m, Style = new CellStyle { Bold = true, NumberFormat = "#,##0" } }
                )
            )
            .Build();

        var bytes = XlsxRenderer.Render(model);
        bytes.Length.Should().BeGreaterThan(0);

        Directory.CreateDirectory(OutputDir);
        File.WriteAllBytes(Path.Combine(OutputDir, "report-q1.xlsx"), bytes);
    }

    [Fact]
    public void Output_StyledSheet()
    {
        var headerStyle = new CellStyle
        {
            Bold = true,
            FontSize = 12,
            FontColor = "FFFFFF",
            BackgroundColor = "4472C4",
            HorizontalAlign = HorizontalAlign.Center
        };

        var currencyStyle = new CellStyle
        {
            NumberFormat = "#,##0.00 \u20AC",
            HorizontalAlign = HorizontalAlign.Right
        };

        var dateStyle = new CellStyle
        {
            NumberFormat = "dd/MM/yyyy"
        };

        var model = new SpreadsheetModel
        {
            Title = "Fatture",
            Sheets =
            [
                new Sheet
                {
                    Name = "Fatture",
                    Columns = [new Column(30), new Column(15), new Column(15), new Column(15)],
                    FrozenPane = new FrozenPane(1),
                    Rows =
                    [
                        new Row(
                        [
                            new Cell { Value = "Cliente", Style = headerStyle },
                            new Cell { Value = "Data", Style = headerStyle },
                            new Cell { Value = "Importo", Style = headerStyle },
                            new Cell { Value = "Stato", Style = headerStyle }
                        ]),
                        new Row(
                        [
                            new Cell { Value = "Acme Corp" },
                            new Cell { Value = new DateTime(2026, 1, 15), Style = dateStyle },
                            new Cell { Value = 4500.00m, Style = currencyStyle },
                            new Cell { Value = "Pagata" }
                        ]),
                        new Row(
                        [
                            new Cell { Value = "Globex Inc" },
                            new Cell { Value = new DateTime(2026, 2, 28), Style = dateStyle },
                            new Cell { Value = 12300.50m, Style = currencyStyle },
                            new Cell { Value = "In attesa" }
                        ]),
                        new Row(
                        [
                            new Cell { Value = "Wayne Enterprises" },
                            new Cell { Value = new DateTime(2026, 3, 10), Style = dateStyle },
                            new Cell { Value = 89000.00m, Style = currencyStyle },
                            new Cell { Value = "Pagata" }
                        ])
                    ]
                }
            ]
        };

        var bytes = XlsxRenderer.Render(model);

        Directory.CreateDirectory(OutputDir);
        File.WriteAllBytes(Path.Combine(OutputDir, "fatture-styled.xlsx"), bytes);
    }
}
