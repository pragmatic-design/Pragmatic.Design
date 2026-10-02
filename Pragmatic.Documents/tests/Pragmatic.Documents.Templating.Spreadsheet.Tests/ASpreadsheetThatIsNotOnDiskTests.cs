using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Xlsx;

namespace Pragmatic.Documents.Templating.Spreadsheet.Tests;

/// <summary>
///     A template's data can come from somewhere that is not a path.
/// </summary>
/// <remarks>
///     <para>
///         An application with uploaded template assets — Casework, where an organisation sends in its
///         own letter — keeps them in <c>IFileStorage</c>, which is local disk in the intake host and
///         object storage in a deployment. A data source that takes a path cannot read that without
///         reaching past the abstraction, and an example that did would be teaching exactly that.
///     </para>
///     <para>
///         So the data sources take a stream, and the file-backed ones are the convenience over them —
///         <c>SpreadsheetPath.Validate</c> and its <c>allowedRoot</c> stay with those, for the
///         deployment-asset case.
///     </para>
///     <para>
///         ⚠️ <b>The cache is a deliberate difference, not an oversight.</b> The XLSX source reads and
///         parses once per instance and shares the result; the CSV source does not. The file-backed
///         pair behaves the same way, and it has a reason — an XLSX is a zip full of XML and a CSV is a
///         stream of lines — so the stream sources keep it rather than quietly making the two agree.
///         For a stream source the instance, not a path, is the cache key: a caller who wants the data
///         re-read builds another source.
///     </para>
/// </remarks>
public class ASpreadsheetThatIsNotOnDiskTests
{
    [Fact]
    public async Task ACsvSourceReadsWhateverTheStreamGives()
    {
        // Nothing on disk, and nothing that could be: the bytes are held by the caller, exactly as
        // IFileStorage hands them back.
        var stored = Encoding.UTF8.GetBytes("Name,Total\r\nWidget,99.5\r\nGadget,49.0");

        var provider = new CsvStreamDataSource("fees", _ => Task.FromResult<Stream>(new MemoryStream(stored)));

        provider.Name.Should().Be("fees");
        provider.ValueType.Should().Be(typeof(SpreadsheetModel));

        var model = (SpreadsheetModel)(await provider.ResolveAsync())!;

        model.Sheets.Should().ContainSingle();
        // ⚠️ Three, not two: CsvReader.ReadAsModel keeps the header as a row of the model. Assumed
        // otherwise when this case was written, and the run said so — the reader hands the template a
        // grid, and deciding which row is a heading is the template's business.
        model.Sheets[0].Rows.Should().HaveCount(3);
        model.Sheets[0].Rows[1].Cells[0].Value.Should().Be("Widget",
            "the data follows the header, which is what a template addressing row 2 relies on");
    }

    [Fact]
    public async Task AnXlsxSourceReadsWhateverTheStreamGives()
    {
        var stored = XlsxBytes();

        var provider = new XlsxStreamDataSource("report", _ => Task.FromResult<Stream>(new MemoryStream(stored)));

        var model = (SpreadsheetModel)(await provider.ResolveAsync())!;

        model.Sheets.Should().ContainSingle();
        model.Sheets[0].Rows.Should().HaveCount(3, "header row included, as the CSV reader does too");
    }

    /// <summary>
    ///     The stream is opened when the data is resolved, not when the source is built.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The property that makes this usable over storage at all. A template catalogue is
    ///     assembled before anyone knows whether a given source will be read — a letter that never
    ///     mentions the fee table must not fetch it — and a factory called at construction would turn
    ///     every declared source into a download.
    /// </remarks>
    [Fact]
    public async Task TheStreamIsOpenedOnResolve_NotOnConstruction()
    {
        var opened = 0;
        var provider = new CsvStreamDataSource("fees", _ =>
        {
            opened++;
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("A\r\n1")));
        });

        opened.Should().Be(0, "building the source must not touch storage");

        await provider.ResolveAsync();

        opened.Should().Be(1);
    }

    /// <summary>
    ///     The XLSX source reads once per instance, and the CSV source does not.
    /// </summary>
    /// <remarks>
    ///     Both halves asserted, because the difference is the decision. Asserting only the cache
    ///     would leave "reads once" satisfied by a source that never re-reads anything, and asserting
    ///     only the CSV would leave the XLSX free to re-parse a zip per placeholder.
    /// </remarks>
    [Fact]
    public async Task TheXlsxSourceReadsOncePerInstance_AndTheCsvSourceDoesNot()
    {
        var xlsxOpens = 0;
        var xlsx = new XlsxStreamDataSource("report", _ =>
        {
            xlsxOpens++;
            return Task.FromResult<Stream>(new MemoryStream(XlsxBytes()));
        });

        await xlsx.ResolveAsync();
        await xlsx.ResolveAsync();

        xlsxOpens.Should().Be(1, "an XLSX is a zip full of XML, and the parse is shared");

        var csvOpens = 0;
        var csv = new CsvStreamDataSource("fees", _ =>
        {
            csvOpens++;
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("A\r\n1")));
        });

        await csv.ResolveAsync();
        await csv.ResolveAsync();

        csvOpens.Should().Be(2,
            "the CSV source does not cache, which is what its file-backed counterpart does — the "
            + "stream sources inherit the difference rather than quietly settling it");
    }

    [Fact]
    public async Task TheCatalogTakesAStreamSourceLikeAnyOther()
    {
        var catalog = new DataSourceCatalog();
        catalog.AddCsvStream("fees", _ => Task.FromResult<Stream>(
            new MemoryStream(Encoding.UTF8.GetBytes("Name,Total\r\nWidget,99.5"))));

        catalog.HasSource("fees").Should().BeTrue();

        var value = await catalog.ToDataContext().ResolveAsync("fees");

        value.Should().BeOfType<SpreadsheetModel>();
    }

    /// <summary>
    ///     The control: the file-backed source still works, and is now the convenience over the stream.
    /// </summary>
    /// <remarks>
    ///     Without it, "the package takes a stream" is satisfied by a change that broke the
    ///     deployment-asset case the package was built for — which the decision explicitly kept.
    /// </remarks>
    [Fact]
    public async Task TheFileBackedSourceStillReadsAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prag557-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, "Name,Total\r\nWidget,99.5\r\nGadget,49.0");

        try
        {
            var model = (SpreadsheetModel)(await new CsvFileDataSource("fees", path).ResolveAsync())!;

            model.Sheets[0].Rows.Should().HaveCount(3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] XlsxBytes()
        => XlsxRenderer.Render(new SpreadsheetBuilder()
            .Sheet("Fees", s => s
                .HeaderRow("Name", "Total")
                .Row("Widget", 99.5)
                .Row("Gadget", 49.0))
            .Build());
}
