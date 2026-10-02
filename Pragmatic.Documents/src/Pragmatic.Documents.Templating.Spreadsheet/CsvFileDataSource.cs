using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templating.Data;

namespace Pragmatic.Documents.Templating.Spreadsheet;

/// <summary>
/// Data source that reads a CSV file into a <see cref="SpreadsheetModel"/>.
/// </summary>
/// <remarks>
///     The convenience over <see cref="CsvStreamDataSource" />, for the deployment-asset case this
///     package was built for: a spreadsheet the operator puts beside the application. What a tenant
///     uploads does not live at a path — it lives in <c>IFileStorage</c> — and that is what the stream
///     form is for. <see cref="SpreadsheetPath.Validate" /> and its <c>allowedRoot</c> stay
///     here, where there is a path to confine.
/// </remarks>
public sealed class CsvFileDataSource : IDataSourceProvider
{
    private readonly string _filePath;
    private readonly CsvOptions _options;

    public string Name { get; }
    public Type ValueType => typeof(SpreadsheetModel);

    public CsvFileDataSource(string name, string filePath, CsvOptions? options = null, string? allowedRoot = null)
    {
        Name = name;
        // Canonicalize the path and, when an allowedRoot is supplied, reject any path that escapes it.
        // The path comes from template configuration, which may be assembled from less-trusted input.
        _filePath = SpreadsheetPath.Validate(filePath, allowedRoot);
        _options = options ?? CsvOptions.Default;
    }

    public async ValueTask<object?> ResolveAsync(CancellationToken ct = default)
    {
        // ⚠️ Still the whole read on the thread pool, and not `new CsvStreamDataSource(…)` over
        // File.OpenRead: the parse is synchronous too, and offloading only the open would leave it on
        // the caller's thread. The sheet keeps the file's own name, which the stream form cannot know.
        var model = await Task.Run(() =>
        {
            using var stream = File.OpenRead(_filePath);
            return CsvReader.ReadAsModel(stream, Path.GetFileNameWithoutExtension(_filePath), _options);
        }, ct).ConfigureAwait(false);
        return model;
    }
}
