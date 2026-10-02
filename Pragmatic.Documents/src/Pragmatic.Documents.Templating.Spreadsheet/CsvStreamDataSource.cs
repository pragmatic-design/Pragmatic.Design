using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templating.Data;

namespace Pragmatic.Documents.Templating.Spreadsheet;

/// <summary>
///     Data source that reads a CSV from wherever the caller can open a stream.
/// </summary>
/// <remarks>
///     <para>
///         The general form, and <see cref="CsvFileDataSource" /> is the convenience over it. An
///         application with uploaded template assets keeps them in <c>IFileStorage</c> — local disk in
///         one host, object storage in a deployment — and a <b>path</b> cannot reach that without going
///         around the abstraction.
///     </para>
///     <para>
///         ⚠️ The stream is opened when the data is <b>resolved</b>, not when the source is built. A
///         template catalogue is assembled before anyone knows which sources a given document will
///         mention, and a factory called at construction would turn every declared source into a
///         download — including the ones the letter never names.
///     </para>
///     <para>
///         ⚠️ <b>No caching</b>, deliberately, and the same as the file-backed source: a CSV is a
///         stream of lines and re-reading it is cheap. <see cref="XlsxStreamDataSource" /> does cache,
///         because an XLSX is a zip full of XML. The difference is inherited rather than settled,
///         because settling it silently would change one of the two for callers who never asked.
///     </para>
/// </remarks>
/// <param name="name">The name the template refers to this data by.</param>
/// <param name="open">Opens the CSV. Called once per resolve; the stream is disposed here.</param>
/// <param name="options">CSV dialect, or the default.</param>
public sealed class CsvStreamDataSource(
    string name,
    Func<CancellationToken, Task<Stream>> open,
    CsvOptions? options = null)
    : IDataSourceProvider
{
    private readonly Func<CancellationToken, Task<Stream>> _open =
        open ?? throw new ArgumentNullException(nameof(open));

    private readonly CsvOptions _options = options ?? CsvOptions.Default;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public Type ValueType => typeof(SpreadsheetModel);

    /// <inheritdoc />
    public async ValueTask<object?> ResolveAsync(CancellationToken ct = default)
    {
        var stream = await _open(ct).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            // The sheet is named after the source, not after a file: there may be no file. The
            // file-backed overload passes the file's own name so nothing about it changes.
            return CsvReader.ReadAsModel(stream, Name, _options);
        }
    }
}
