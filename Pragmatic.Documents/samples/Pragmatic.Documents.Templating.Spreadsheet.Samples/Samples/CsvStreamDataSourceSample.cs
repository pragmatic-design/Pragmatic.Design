using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templating.Data;

namespace Pragmatic.Documents.Templating.Spreadsheet.Samples.Samples;

/// <summary>
///     Register a CSV that is <b>not on disk</b> as a named DataSource.
/// </summary>
/// <remarks>
///     <para>
///         The shape to reach for when a tenant <b>uploads</b> a spreadsheet: what they sent in lives
///         behind <c>IFileStorage</c> — local disk in one deployment, object storage in another — and
///         has no path the application is entitled to compose. The file-backed sources are the
///         convenience for the other case, a workbook the operator puts beside the application.
///     </para>
///     <para>
///         ⚠️ The factory runs when the source is <b>resolved</b>, not when it is registered. A
///         catalogue is assembled before anyone knows which sources a document will name, so a
///         template that never mentions this one never opens the stream.
///     </para>
/// </remarks>
public static class CsvStreamDataSourceSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- CSV from a stream (no path) ---");

        // 1. The bytes, as they would come back from storage rather than from a path.
        string[] headers = ["Charge", "Amount"];
        var rows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Late filing", "45.00" },
            new string?[] { "Copy of decision", "12.50" },
        };
        var stored = CsvWriter.WriteToArray(headers, rows);
        Console.WriteLine($"  bytes held in storage  : {stored.Length}");

        // 2. Register it. Nothing is opened yet.
        var opened = 0;
        var catalog = new DataSourceCatalog()
            .AddCsvStream("fees", _ =>
            {
                opened++;
                return Task.FromResult<Stream>(new MemoryStream(stored));
            });
        Console.WriteLine($"  opened after register  : {opened}");

        // 3. Resolving is what reads it.
        var model = (SpreadsheetModel)(await catalog.ToDataContext().ResolveAsync("fees"))!;
        Console.WriteLine($"  opened after resolve   : {opened}");

        var sheet = model.Sheets[0];
        Console.WriteLine($"  resolved rows          : {sheet.Rows.Count} (incl. header)");
        for (var i = 1; i < sheet.Rows.Count; i++)
            Console.WriteLine($"    - {sheet.Rows[i].Cells[0].Value,-20} {sheet.Rows[i].Cells[1].Value}");

        Console.WriteLine();
    }
}
