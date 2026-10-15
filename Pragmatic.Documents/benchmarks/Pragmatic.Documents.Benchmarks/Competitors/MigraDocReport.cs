using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Pragmatic.Documents.Benchmarks.Workload;

namespace Pragmatic.Documents.Benchmarks.Competitors;

/// <summary>The report written with MigraDoc: the heading and the table, with the header repeated on every page.</summary>
/// <remarks>
///     MigraDoc has no template language: the document is built in code from the typed lines, which is how a
///     user of it would write this report. Column widths follow the template's, in millimetres.
/// </remarks>
public static class MigraDocReport
{
    private static readonly double[] Widths = [10, 18, 30, 18, 10, 16, 14, 10, 16, 22];

    /// <summary>Writes the PDF into <paramref name="output" />, from its start, and returns its length.</summary>
    public static long Render(InvoiceReport report, MemoryStream output)
    {
        var document = new Document();
        document.Info.Title = report.Title;
        var style = document.Styles[StyleNames.Normal]!;
        style.Font.Name = "Sans";
        style.Font.Size = 8;

        var section = document.AddSection();
        section.AddParagraph(report.Title).Format.Font.Bold = true;

        var table = section.AddTable();
        table.Borders.Width = 0.25;
        foreach (var width in Widths)
            table.AddColumn(Unit.FromMillimeter(width));

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        for (var c = 0; c < Workloads.Fields; c++)
            header.Cells[c].AddParagraph(Workloads.Headers[c]);

        // Invariant, as the template's data context formats by default.
        var invariant = CultureInfo.InvariantCulture;
        foreach (var l in report.Lines)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(l.Number.ToString(invariant));
            row.Cells[1].AddParagraph(l.Sku);
            row.Cells[2].AddParagraph(l.Description);
            row.Cells[3].AddParagraph(l.Category);
            row.Cells[4].AddParagraph(l.Quantity.ToString(invariant));
            row.Cells[5].AddParagraph(l.UnitPrice.ToString(invariant));
            row.Cells[6].AddParagraph(l.Discount.ToString(invariant));
            row.Cells[7].AddParagraph(l.VatRate.ToString(invariant));
            row.Cells[8].AddParagraph(l.Net.ToString(invariant));
            row.Cells[9].AddParagraph(l.ShippedOn.ToString(invariant));
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        output.SetLength(0);
        renderer.PdfDocument.Save(output, closeStream: false);

        return output.Length;
    }
}
