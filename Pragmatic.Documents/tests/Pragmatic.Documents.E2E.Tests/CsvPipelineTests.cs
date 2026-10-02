using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Spreadsheet;
using Pragmatic.Documents.Xlsx;

namespace Pragmatic.Documents.E2E.Tests;

// --- SG-generated DTOs (realistic Showcase scenario) ---

[CsvSerializable]
public partial record InvoiceExportDto
{
    [CsvColumn("Numero Fattura")]
    public string Number { get; init; } = "";

    [CsvColumn("Data", Format = "dd/MM/yyyy")]
    public DateTime Date { get; init; }

    [CsvColumn("Cliente")]
    public string CustomerName { get; init; } = "";

    [CsvColumn("P.IVA")]
    public string VatId { get; init; } = "";

    [CsvColumn("Imponibile", Format = "#,##0.00")]
    public decimal Subtotal { get; init; }

    [CsvColumn("IVA", Format = "#,##0.00")]
    public decimal Vat { get; init; }

    [CsvColumn("Totale", Format = "#,##0.00")]
    public decimal Total { get; init; }

    [CsvColumn("Stato")]
    public string Status { get; init; } = "";

    [CsvColumn("Pagata")]
    public bool Paid { get; init; }
}

[CsvSerializable]
public partial record GuestExportDto
{
    [CsvColumn("Nome")]
    public string FirstName { get; init; } = "";

    [CsvColumn("Cognome")]
    public string LastName { get; init; } = "";

    [CsvColumn("Email")]
    public string Email { get; init; } = "";

    [CsvColumn("Check-In", Format = "dd/MM/yyyy")]
    public DateTime CheckIn { get; init; }

    [CsvColumn("Check-Out", Format = "dd/MM/yyyy")]
    public DateTime CheckOut { get; init; }

    [CsvColumn("Camera")]
    public int RoomNumber { get; init; }

    [CsvColumn("Importo", Format = "#,##0.00")]
    public decimal Amount { get; init; }
}

/// <summary>
/// E2E tests: SG CSV serializer + XLSX export + DataSourceCatalog integration.
/// Simulates realistic Showcase scenarios (Billing invoice export, Booking guest list).
/// </summary>
public class CsvPipelineTests
{
    private static readonly string OutputDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "output");

    // --- Scenario 1: Billing — Export invoices as CSV (Italian format) ---

    [Fact]
    public void Billing_ExportInvoices_CsvItalian()
    {
        var invoices = GenerateInvoices();
        var options = CsvOptions.Italian;

        // SG-generated write
        var csv = GetString(s => InvoiceExportDto.Csv.Write(s, invoices, options));

        // Verify headers
        csv.Should().Contain("Numero Fattura;Data;Cliente;P.IVA;Imponibile;IVA;Totale;Stato;Pagata");

        // Verify Italian number formatting (comma decimal)
        csv.Should().Contain("4.500,00"); // Subtotal formatted
        csv.Should().Contain("990,00");   // VAT formatted
        csv.Should().Contain("5.490,00"); // Total formatted

        // Verify date formatting
        csv.Should().Contain("15/01/2026");

        Save(csv, "fatture-export.csv");
    }

    [Fact]
    public void Billing_CsvRoundtrip_Italian()
    {
        var invoices = GenerateInvoices();
        var options = CsvOptions.Italian;

        // Write
        using var ms = new MemoryStream();
        InvoiceExportDto.Csv.Write(ms, invoices, options);
        ms.Position = 0;

        // Read back
        var imported = InvoiceExportDto.Csv.Read(ms, options);

        imported.Should().HaveCount(invoices.Length);
        imported[0].Number.Should().Be("2026-001");
        imported[0].CustomerName.Should().Be("Acme S.r.l.");
        imported[0].Subtotal.Should().Be(4500.00m);
        imported[0].Total.Should().Be(5490.00m);
        imported[0].Paid.Should().BeTrue();
        imported[2].Paid.Should().BeFalse();
    }

    // --- Scenario 2: Booking — Export guest list + convert to XLSX ---

    [Fact]
    public void Booking_ExportGuests_CsvThenXlsx()
    {
        var guests = GenerateGuests();
        var options = CsvOptions.Italian;

        // 1. Write CSV
        var csvBytes = GuestExportDto.Csv.WriteToArray(guests, options);
        csvBytes.Should().NotBeEmpty();

        // 2. Read CSV into SpreadsheetModel
        var model = CsvReader.ReadAsModel(csvBytes, "Ospiti", options);
        model.Sheets.Should().HaveCount(1);
        model.Sheets[0].Name.Should().Be("Ospiti");
        model.Sheets[0].Rows.Should().HaveCount(4); // 1 header + 3 data

        // 3. Convert to XLSX (enriched with column widths)
        var enriched = new SpreadsheetBuilder()
            .Title("Lista Ospiti — Aprile 2026")
            .Author("Pragmatic Booking")
            .Sheet("Ospiti", s =>
            {
                s.Column(width: 15).Column(width: 15).Column(width: 25)
                 .Column(width: 12).Column(width: 12).Column(width: 10).Column(width: 12);
                s.FreezeRows(1);

                // Copy rows from CSV model
                foreach (var row in model.Sheets[0].Rows)
                    s.Row(row);
            })
            .Build();

        var xlsxBytes = XlsxRenderer.Render(enriched);

        // 4. Verify XLSX roundtrip
        var readBack = XlsxReader.Read(xlsxBytes);
        readBack.Title.Should().Be("Lista Ospiti — Aprile 2026");
        readBack.Sheets[0].FrozenPane!.Value.Rows.Should().Be(1);

        Save(Encoding.UTF8.GetString(csvBytes), "ospiti-export.csv");
        SaveBytes(xlsxBytes, "ospiti-export.xlsx");
    }

    // --- Scenario 3: DataSourceCatalog — CSV file as template data source ---

    [Fact]
    public async Task DataSourceCatalog_CsvFile_ResolvesAsSpreadsheet()
    {
        var guests = GenerateGuests();
        var options = CsvOptions.Italian;

        // Write CSV fixture file
        var csvPath = Path.Combine(OutputDir, "catalog-guests.csv");
        Directory.CreateDirectory(OutputDir);
        using (var fs = File.Create(csvPath))
            GuestExportDto.Csv.Write(fs, guests, options);

        // Register as DataSource
        var catalog = new DataSourceCatalog()
            .AddCsvFile("guests", csvPath, options);

        catalog.HasSource("guests").Should().BeTrue();

        // Resolve via DataContext
        var ctx = catalog.ToDataContext();
        var value = await ctx.ResolveAsync("guests");
        value.Should().BeOfType<SpreadsheetModel>();

        var model = (SpreadsheetModel)value!;
        model.Sheets[0].Rows.Should().HaveCount(4); // header + 3 guests
    }

    // --- Scenario 4: Full pipeline — Data → SG CSV → XLSX with styles ---

    [Fact]
    public void FullPipeline_InvoicesToStyledXlsx()
    {
        var invoices = GenerateInvoices();
        var options = CsvOptions.Italian;

        // 1. SG CSV write + read to get SpreadsheetModel
        var csvBytes = InvoiceExportDto.Csv.WriteToArray(invoices, options);
        var csvModel = CsvReader.ReadAsModel(csvBytes, "Fatture", options);

        // 2. Build styled XLSX from data
        var headerStyle = new CellStyle
        {
            Bold = true,
            FontColor = "FFFFFF",
            BackgroundColor = "2F5496",
            HorizontalAlign = HorizontalAlign.Center
        };

        var currencyStyle = new CellStyle
        {
            NumberFormat = "#,##0.00 \u20AC",
            HorizontalAlign = HorizontalAlign.Right
        };

        var xlsx = new SpreadsheetBuilder()
            .Title("Export Fatture Q1 2026")
            .Author("Pragmatic Billing")
            .Sheet("Fatture", s =>
            {
                s.Column(width: 15).Column(width: 12).Column(width: 20)
                 .Column(width: 15).Column(width: 15).Column(width: 12)
                 .Column(width: 15).Column(width: 10).Column(width: 8);
                s.FreezeRows(1);

                // Styled header
                s.StyledRow(
                    InvoiceExportDto.Csv.Headers.Select(h =>
                        new Cell { Value = h, Style = headerStyle }).ToArray());

                // Data rows with currency formatting
                foreach (var inv in invoices)
                {
                    s.StyledRow(
                        new Cell { Value = inv.Number },
                        new Cell { Value = inv.Date, Style = new CellStyle { NumberFormat = "dd/MM/yyyy" } },
                        new Cell { Value = inv.CustomerName },
                        new Cell { Value = inv.VatId },
                        new Cell { Value = inv.Subtotal, Style = currencyStyle },
                        new Cell { Value = inv.Vat, Style = currencyStyle },
                        new Cell { Value = inv.Total, Style = currencyStyle },
                        new Cell { Value = inv.Status },
                        new Cell { Value = inv.Paid });
                }

                // Totals row with formulas
                var lastRow = invoices.Length + 1;
                s.StyledRow(
                    new Cell { Value = "TOTALE", Style = new CellStyle { Bold = true } },
                    new Cell { Value = "" },
                    new Cell { Value = "" },
                    new Cell { Value = "" },
                    new Cell { Formula = $"SUM(E2:E{lastRow})", Style = new CellStyle { Bold = true, NumberFormat = "#,##0.00 \u20AC" } },
                    new Cell { Formula = $"SUM(F2:F{lastRow})", Style = new CellStyle { Bold = true, NumberFormat = "#,##0.00 \u20AC" } },
                    new Cell { Formula = $"SUM(G2:G{lastRow})", Style = new CellStyle { Bold = true, NumberFormat = "#,##0.00 \u20AC" } },
                    new Cell { Value = "" },
                    new Cell { Value = "" });
            })
            .Build();

        var xlsxBytes = XlsxRenderer.Render(xlsx);
        xlsxBytes.Length.Should().BeGreaterThan(0);

        SaveBytes(xlsxBytes, "fatture-styled-pipeline.xlsx");
    }

    // --- Test data ---

    private static InvoiceExportDto[] GenerateInvoices() =>
    [
        new()
        {
            Number = "2026-001", Date = new DateTime(2026, 1, 15),
            CustomerName = "Acme S.r.l.", VatId = "IT01234567890",
            Subtotal = 4500.00m, Vat = 990.00m, Total = 5490.00m,
            Status = "Pagata", Paid = true
        },
        new()
        {
            Number = "2026-002", Date = new DateTime(2026, 2, 28),
            CustomerName = "Globex Italia S.p.A.", VatId = "IT09876543210",
            Subtotal = 12300.00m, Vat = 2706.00m, Total = 15006.00m,
            Status = "Pagata", Paid = true
        },
        new()
        {
            Number = "2026-003", Date = new DateTime(2026, 3, 10),
            CustomerName = "Wayne Enterprises S.r.l.", VatId = "IT11223344556",
            Subtotal = 89000.00m, Vat = 19580.00m, Total = 108580.00m,
            Status = "In attesa", Paid = false
        }
    ];

    private static GuestExportDto[] GenerateGuests() =>
    [
        new()
        {
            FirstName = "Marco", LastName = "Rossi", Email = "marco@example.com",
            CheckIn = new DateTime(2026, 4, 1), CheckOut = new DateTime(2026, 4, 5),
            RoomNumber = 201, Amount = 480.00m
        },
        new()
        {
            FirstName = "Giulia", LastName = "Bianchi", Email = "giulia@example.com",
            CheckIn = new DateTime(2026, 4, 3), CheckOut = new DateTime(2026, 4, 7),
            RoomNumber = 305, Amount = 720.00m
        },
        new()
        {
            FirstName = "Luca", LastName = "Verdi", Email = "luca@example.com",
            CheckIn = new DateTime(2026, 4, 5), CheckOut = new DateTime(2026, 4, 8),
            RoomNumber = 102, Amount = 360.00m
        }
    ];

    // --- Helpers ---

    private static void Save(string content, string fileName)
    {
        Directory.CreateDirectory(OutputDir);
        File.WriteAllText(Path.Combine(OutputDir, fileName), content, Encoding.UTF8);
    }

    private static void SaveBytes(byte[] bytes, string fileName)
    {
        Directory.CreateDirectory(OutputDir);
        File.WriteAllBytes(Path.Combine(OutputDir, fileName), bytes);
    }

    private static string GetString(Action<Stream> write)
    {
        using var ms = new MemoryStream();
        write(ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
