using Microsoft.AspNetCore.Http;
using Pragmatic.Documents.Csv;
using Pragmatic.Messaging.Batch;
using Warehouse.Stock.Imports.Messages;

namespace Warehouse.Stock.Imports.Actions;

/// <summary>
///     A supplier sends its stock file: <c>sku,location,quantity</c>, one receipt per row. The file is cut
///     into parts of <see cref="PartSize" /> rows and each part is published on the broker, so both Stock
///     instances apply it together, and <c>GET api/imports/{id}</c> watches it happen.
/// </summary>
/// <remarks>
///     <para>
///         The header is checked here, and a file without the three columns, or without rows, is refused
///         whole: nothing is dispatched. The rows are checked by the part that applies them
///         (<c>ApplyImportPartAction</c>), where the catalogue is — an invalid row is reported with its
///         line, and the others are applied.
///     </para>
///     <para>
///         202: the answer comes before the work. The import id is the batch id the dispatcher returns, and
///         every part carries it in its headers, so the progress store and the parts' own records answer
///         to the same id.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(StockPermissions.StockLevel.Receive)]
[Endpoint(HttpVerb.Post, "api/imports")]
[HttpStatus(202)]
public partial class StartImportAction : DomainAction<ImportStartedDto, ImportFileUnreadableError>
{
    private static readonly string[] Header = ["sku", "location", "quantity"];

    private IBatchDispatcher<StockImport, ImportFilePart> _dispatcher = null!;

    /// <summary>The supplier's CSV.</summary>
    [FromForm]
    [MaxFileSize(16 * 1024 * 1024)]
    [AllowedContentTypes("text/csv", "text/plain", "application/octet-stream")]
    public required IFormFile File { get; init; }

    /// <summary>How many rows a part carries.</summary>
    [FromForm]
    [Range(1, 10_000)]
    public int PartSize { get; init; } = 100;

    public override async Task<Result<ImportStartedDto, IError>> Execute(CancellationToken ct = default)
    {
        string[] headers;
        List<string[]> rows;
        var stream = File.OpenReadStream();
        await using (stream.ConfigureAwait(false))
            (headers, rows) = CsvReader.Read(stream);

        if (!headers.Select(h => h.Trim()).SequenceEqual(Header, StringComparer.OrdinalIgnoreCase))
            return new ImportFileUnreadableError
            {
                Reason = $"the header is '{string.Join(',', headers)}', not '{string.Join(',', Header)}'",
            };

        if (rows.Count == 0)
            return new ImportFileUnreadableError { Reason = "the file has a header and no rows" };

        // The header is line 1, so the first row is line 2: the number a supplier finds in their editor.
        var numbered = rows
            .Select((fields, i) => new ImportFileRow(
                i + 2, Field(fields, 0), Field(fields, 1), Field(fields, 2)))
            .ToList();

        var parts = numbered
            .Chunk(PartSize)
            .Select((chunk, index) => new ImportFilePart(index, [.. chunk]))
            .ToList();

        var importId = await _dispatcher
            .DispatchAsync(new StockImport(parts), label: $"stock import, {numbered.Count} rows", ct)
            .ConfigureAwait(false);
        return new ImportStartedDto { ImportId = importId, Rows = numbered.Count, Parts = parts.Count };
    }

    /// <summary>A field of the row, trimmed, or empty when the row is short.</summary>
    private static string Field(string[] fields, int index) => index < fields.Length ? fields[index].Trim() : "";
}
