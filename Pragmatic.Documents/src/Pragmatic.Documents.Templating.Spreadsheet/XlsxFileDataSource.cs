using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Xlsx;

namespace Pragmatic.Documents.Templating.Spreadsheet;

/// <summary>
/// Data source that reads an XLSX file into a <see cref="SpreadsheetModel"/>.
/// The file is read and parsed once; the result is cached and shared across all
/// (possibly concurrent) <see cref="ResolveAsync"/> calls.
/// </summary>
/// <remarks>
///     The convenience over <see cref="XlsxStreamDataSource" />, for the deployment-asset case: a
///     workbook the operator puts beside the application. What a tenant uploads lives in
///     <c>IFileStorage</c>, and that is what the stream form is for.
/// </remarks>
public sealed class XlsxFileDataSource : IDataSourceProvider
{
    private readonly string _filePath;

    // Read + parse exactly once, even under concurrent resolves. ExecutionAndPublication
    // guarantees a single execution of the factory and a single published Task; subsequent
    // resolves await the already-completed task without re-reading the file.
    private readonly Lazy<Task<object?>> _cached;

    public string Name { get; }
    public Type ValueType => typeof(SpreadsheetModel);

    public XlsxFileDataSource(string name, string filePath, string? allowedRoot = null)
    {
        Name = name;
        // Canonicalize and, when an allowedRoot is supplied, reject any path that escapes it
        // (same trust boundary as CsvFileDataSource).
        _filePath = SpreadsheetPath.Validate(filePath, allowedRoot);
        _cached = new Lazy<Task<object?>>(LoadAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async ValueTask<object?> ResolveAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await _cached.Value.ConfigureAwait(false);
    }

    private async Task<object?> LoadAsync()
    {
        // Read without a CancellationToken: the result is shared, so a single caller's
        // cancellation must not poison the cached value for everyone else.
        var bytes = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
        return XlsxReader.Read(bytes);
    }
}
