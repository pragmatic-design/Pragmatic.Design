using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Xlsx;

namespace Pragmatic.Documents.Templating.Spreadsheet;

/// <summary>
///     Data source that reads an XLSX from wherever the caller can open a stream.
/// </summary>
/// <remarks>
///     <para>
///         The general form, and <see cref="XlsxFileDataSource" /> is the convenience over it — see
///         <see cref="CsvStreamDataSource" /> for why the package grew one.
///     </para>
///     <para>
///         ⚠️ <b>The instance is the cache key</b>, where the file-backed source's was effectively the
///         path. Read and parsed once, shared by every resolve, because an XLSX is a zip full of XML
///         and a document mentioning three columns would otherwise unpack it three times. A caller who
///         needs the data re-read builds another source — which is the same thing the file-backed one
///         has always asked for, said out loud now that there is no path to reason about.
///     </para>
///     <para>
///         ⚠️ The stream is opened on the first <b>resolve</b>, not on construction: a catalogue is
///         assembled before anyone knows which sources a document will name.
///     </para>
/// </remarks>
public sealed class XlsxStreamDataSource : IDataSourceProvider
{
    private readonly Func<CancellationToken, Task<Stream>> _open;

    // Read + parse exactly once, even under concurrent resolves. ExecutionAndPublication guarantees a
    // single execution of the factory and a single published Task; later resolves await the completed
    // task without opening the stream again.
    private readonly Lazy<Task<object?>> _cached;

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Type ValueType => typeof(SpreadsheetModel);

    /// <param name="name">The name the template refers to this data by.</param>
    /// <param name="open">Opens the workbook. Called once; the stream is disposed here.</param>
    public XlsxStreamDataSource(string name, Func<CancellationToken, Task<Stream>> open)
    {
        Name = name;
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _cached = new Lazy<Task<object?>>(LoadAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public async ValueTask<object?> ResolveAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await _cached.Value.ConfigureAwait(false);
    }

    private async Task<object?> LoadAsync()
    {
        // No CancellationToken: the result is shared, so one caller's cancellation must not poison the
        // cached value for everyone else. The same reason the file-backed source reads without one.
        var stream = await _open(CancellationToken.None).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            return XlsxReader.Read(buffer.ToArray());
        }
    }
}
