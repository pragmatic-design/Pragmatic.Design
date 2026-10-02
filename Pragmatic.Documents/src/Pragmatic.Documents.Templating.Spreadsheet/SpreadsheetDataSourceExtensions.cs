using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templating.Data;

namespace Pragmatic.Documents.Templating.Spreadsheet;

/// <summary>
/// Extension methods on <see cref="DataSourceCatalog"/> for spreadsheet data sources.
/// </summary>
public static class SpreadsheetDataSourceExtensions
{
    extension(DataSourceCatalog catalog)
    {
        /// <summary>
        /// Add an XLSX file as a data source (reads into SpreadsheetModel on demand). Pass
        /// <paramref name="allowedRoot"/> to confine the file to a directory (rejects traversal outside it).
        /// </summary>
        public DataSourceCatalog AddXlsxFile(string name, string filePath, string? allowedRoot = null)
            => catalog.AddProvider(new XlsxFileDataSource(name, filePath, allowedRoot));

        /// <summary>
        /// Add a CSV file as a data source (reads into SpreadsheetModel on demand). Pass
        /// <paramref name="allowedRoot"/> to confine the file to a directory (rejects traversal outside it).
        /// </summary>
        public DataSourceCatalog AddCsvFile(string name, string filePath, CsvOptions? options = null, string? allowedRoot = null)
            => catalog.AddProvider(new CsvFileDataSource(name, filePath, options, allowedRoot));

        /// <summary>
        /// Add an XLSX as a data source, read from wherever the caller can open a stream — object
        /// storage, a blob column, an <c>IFileStorage</c> an organisation uploaded into.
        /// </summary>
        /// <remarks>
        ///     ⚠️ The stream is opened on the first resolve, not here: a catalogue is assembled before
        ///     anyone knows which sources a document will name, and opening now would fetch the ones
        ///     it never mentions. Read once per source instance thereafter.
        /// </remarks>
        public DataSourceCatalog AddXlsxStream(string name, Func<CancellationToken, Task<Stream>> open)
            => catalog.AddProvider(new XlsxStreamDataSource(name, open));

        /// <summary>
        /// Add a CSV as a data source, read from wherever the caller can open a stream.
        /// </summary>
        /// <remarks>
        ///     ⚠️ Opened on every resolve and not cached — the same as <c>AddCsvFile</c>, and unlike
        ///     the XLSX pair, which parses a zip.
        /// </remarks>
        public DataSourceCatalog AddCsvStream(
            string name, Func<CancellationToken, Task<Stream>> open, CsvOptions? options = null)
            => catalog.AddProvider(new CsvStreamDataSource(name, open, options));

        /// <summary>Add a pre-built SpreadsheetModel as a static data source.</summary>
        public DataSourceCatalog AddSpreadsheet(string name, SpreadsheetModel model)
            => catalog.Add(name, model);
    }
}
