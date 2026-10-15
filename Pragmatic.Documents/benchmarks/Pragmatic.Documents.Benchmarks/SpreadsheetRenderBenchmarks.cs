using BenchmarkDotNet.Attributes;
using ClosedXML.Excel;
using Pragmatic.Documents.Benchmarks.Workload;
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Xlsx;

namespace Pragmatic.Documents.Benchmarks;

/// <summary>
///     The report's 1,000 rows of ten fields as an XLSX sheet with a bold header row, written into a reused
///     stream: Pragmatic's builder and renderer, and ClosedXML's workbook.
/// </summary>
/// <remarks>
///     Each row builds its workbook from the same typed lines inside the measured call, as an export does: the
///     model a library needs is part of what it costs. <see cref="Setup" /> reads both files back and checks
///     they hold the same cells, every one of them, before anything is timed.
/// </remarks>
[MemoryDiagnoser]
public class SpreadsheetRenderBenchmarks
{
    private readonly MemoryStream _output = new();
    private List<InvoiceLine> _lines = null!;

    [GlobalSetup]
    public void Setup()
    {
        _lines = Workloads.Typed().Lines;
        VerifyBothWriteTheSameCells();
    }

    [Benchmark(Baseline = true)]
    public long Xlsx_Pragmatic()
    {
        _output.SetLength(0);
        var book = new SpreadsheetBuilder()
            .Sheet("Lines", sheet =>
            {
                sheet.HeaderRow(Workloads.Headers);
                foreach (var l in _lines)
                    sheet.Row(l.Number, l.Sku, l.Description, l.Category, l.Quantity, l.UnitPrice, l.Discount,
                        l.VatRate, l.Net, l.ShippedOn);
            })
            .Build();
        XlsxRenderer.RenderTo(_output, book);

        return _output.Length;
    }

    [Benchmark]
    public long Xlsx_ClosedXml()
    {
        _output.SetLength(0);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Lines");
        for (var c = 0; c < Workloads.Fields; c++)
            sheet.Cell(1, c + 1).Value = Workloads.Headers[c];
        sheet.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var l in _lines)
        {
            sheet.Cell(row, 1).Value = l.Number;
            sheet.Cell(row, 2).Value = l.Sku;
            sheet.Cell(row, 3).Value = l.Description;
            sheet.Cell(row, 4).Value = l.Category;
            sheet.Cell(row, 5).Value = l.Quantity;
            sheet.Cell(row, 6).Value = l.UnitPrice;
            sheet.Cell(row, 7).Value = l.Discount;
            sheet.Cell(row, 8).Value = l.VatRate;
            sheet.Cell(row, 9).Value = l.Net;
            sheet.Cell(row, 10).Value = l.ShippedOn;
            row++;
        }

        workbook.SaveAs(_output);

        return _output.Length;
    }

    // ⚠️ Read back with ClosedXML, not XlsxReader: XlsxReader refuses the absolute relationship target
    // ClosedXML writes (/xl/worksheets/sheet1.xml), which OPC allows (#258).
    private void VerifyBothWriteTheSameCells()
    {
        Xlsx_Pragmatic();
        var pragmatic = ReadBack();
        Xlsx_ClosedXml();
        var closedXml = ReadBack();

        var failures = new List<string>();
        for (var r = 0; r <= Workloads.Rows && failures.Count < 5; r++)
        {
            for (var c = 0; c < Workloads.Fields; c++)
            {
                var a = pragmatic[r][c];
                var b = closedXml[r][c];
                if (!SameValue(a, b))
                    failures.Add($"row {r + 1}, column {c + 1}: '{a}' against '{b}'");
            }
        }

        if (failures.Count > 0)
            throw new InvalidOperationException("The two rows would write different sheets: " + string.Join("; ", failures));
    }

    private object?[][] ReadBack()
    {
        _output.Position = 0;
        using var workbook = new XLWorkbook(_output);
        var sheet = workbook.Worksheet(1);
        var rows = new object?[Workloads.Rows + 1][];
        for (var r = 0; r < rows.Length; r++)
        {
            rows[r] = new object?[Workloads.Fields];
            for (var c = 0; c < Workloads.Fields; c++)
                rows[r][c] = Plain(sheet.Cell(r + 1, c + 1).Value);
        }

        return rows;
    }

    private static object? Plain(XLCellValue value)
    {
        if (value.IsDateTime) return value.GetDateTime();
        if (value.IsNumber) return value.GetNumber();
        if (value.IsText) return value.GetText();

        return value.IsBlank ? null : value.ToString();
    }

    // A date may come back as its serial number when a writer does not mark the cell as a date: compare what
    // the reader can tell apart.
    private static bool SameValue(object? a, object? b) => (a, b) switch
    {
        (DateTime x, double n) => Math.Abs(x.ToOADate() - n) < 1e-9,
        (double m, DateTime y) => Math.Abs(y.ToOADate() - m) < 1e-9,
        (double m, double n) => Math.Abs(m - n) < 1e-9,
        _ => Equals(a, b),
    };
}
