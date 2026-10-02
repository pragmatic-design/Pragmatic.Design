using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Csv;
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Spreadsheet;
using Pragmatic.Documents.Xlsx;

namespace Pragmatic.Documents.Templating.Spreadsheet.Tests;

public class SpreadsheetDataSourceTests
{
    private static readonly string FixturesDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures");

    // --- XLSX DataSource ---

    [Fact]
    public async Task XlsxFileDataSource_ReadsFile()
    {
        var filePath = CreateXlsxFixture("test-ds.xlsx");

        var provider = new XlsxFileDataSource("report", filePath);

        provider.Name.Should().Be("report");
        provider.ValueType.Should().Be(typeof(SpreadsheetModel));

        var result = await provider.ResolveAsync();
        result.Should().BeOfType<SpreadsheetModel>();
        var model = (SpreadsheetModel)result!;
        model.Sheets.Should().HaveCount(1);
        model.Sheets[0].Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task XlsxFileDataSource_InCatalog_ResolvesViaDataContext()
    {
        var filePath = CreateXlsxFixture("test-ds-catalog.xlsx");

        var catalog = new DataSourceCatalog();
        catalog.AddXlsxFile("report", filePath);

        catalog.HasSource("report").Should().BeTrue();

        var ctx = catalog.ToDataContext();
        var value = await ctx.ResolveAsync("report");
        value.Should().BeOfType<SpreadsheetModel>();
    }

    // --- CSV DataSource ---

    [Fact]
    public async Task CsvFileDataSource_ReadsFile()
    {
        var filePath = CreateCsvFixture("test-ds.csv", "Name,Total\r\nWidget,99.5\r\nGadget,49.0");

        var provider = new CsvFileDataSource("data", filePath);

        provider.Name.Should().Be("data");
        provider.ValueType.Should().Be(typeof(SpreadsheetModel));

        var result = await provider.ResolveAsync();
        result.Should().BeOfType<SpreadsheetModel>();
        var model = (SpreadsheetModel)result!;
        model.Sheets.Should().HaveCount(1);
        model.Sheets[0].Rows.Should().HaveCount(3); // header + 2 data rows
    }

    [Fact]
    public async Task CsvFileDataSource_WithOptions()
    {
        var filePath = CreateCsvFixture("test-ds-it.csv", "Prodotto;Totale\r\nWidget;99,5");

        var provider = new CsvFileDataSource("data", filePath, CsvOptions.Italian);

        var result = await provider.ResolveAsync();
        var model = (SpreadsheetModel)result!;
        model.Sheets[0].Rows.Should().HaveCount(2);
    }

    [Fact]
    public void CsvFileDataSource_InCatalog()
    {
        var filePath = CreateCsvFixture("test-ds-cat.csv", "A,B\r\n1,2");

        var catalog = new DataSourceCatalog();
        catalog.AddCsvFile("csv", filePath);

        catalog.HasSource("csv").Should().BeTrue();
    }

    // --- AddSpreadsheet (static model) ---

    [Fact]
    public async Task AddSpreadsheet_RegistersStaticModel()
    {
        var model = new SpreadsheetBuilder()
            .Sheet("Data", s => s.Row("A", "B"))
            .Build();

        var catalog = new DataSourceCatalog();
        catalog.AddSpreadsheet("ss", model);

        var ctx = catalog.ToDataContext();
        var value = await ctx.ResolveAsync("ss");
        value.Should().BeOfType<SpreadsheetModel>();
    }

    // --- Extension method fluent API ---

    [Fact]
    public void Extensions_Fluent_Chaining()
    {
        var xlsxPath = CreateXlsxFixture("chain-xlsx.xlsx");
        var csvPath = CreateCsvFixture("chain-csv.csv", "X\r\n1");

        var catalog = new DataSourceCatalog()
            .AddXlsxFile("xlsx", xlsxPath)
            .AddCsvFile("csv", csvPath)
            .AddSpreadsheet("static", new SpreadsheetModel());

        catalog.Count.Should().Be(3);
        catalog.HasSource("xlsx").Should().BeTrue();
        catalog.HasSource("csv").Should().BeTrue();
        catalog.HasSource("static").Should().BeTrue();
    }

    // --- Helpers ---

    private static string CreateXlsxFixture(string fileName)
    {
        Directory.CreateDirectory(FixturesDir);
        var filePath = Path.Combine(FixturesDir, fileName);

        var model = new SpreadsheetBuilder()
            .Sheet("Data", s => s
                .HeaderRow("Name", "Total")
                .Row("Widget", 99.5)
            )
            .Build();

        File.WriteAllBytes(filePath, XlsxRenderer.Render(model));
        return filePath;
    }

    private static string CreateCsvFixture(string fileName, string content)
    {
        Directory.CreateDirectory(FixturesDir);
        var filePath = Path.Combine(FixturesDir, fileName);
        File.WriteAllText(filePath, content, Encoding.UTF8);
        return filePath;
    }

    // --- Path confinement (allowedRoot) ---

    [Fact]
    public void CsvFileDataSource_WithAllowedRoot_RejectsTraversalOutsideRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "confined-root");
        Directory.CreateDirectory(root);
        var escaping = Path.Combine(root, "..", "secret.csv");

        var act = () => new CsvFileDataSource("x", escaping, allowedRoot: root);

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void CsvFileDataSource_WithAllowedRoot_AllowsPathInsideRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "confined-root2");
        Directory.CreateDirectory(root);
        var inside = Path.Combine(root, "data.csv");

        var act = () => new CsvFileDataSource("x", inside, allowedRoot: root);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddCsvFile_WithAllowedRoot_RejectsTraversal()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "confined-root3");
        Directory.CreateDirectory(root);
        var escaping = Path.Combine(root, "..", "secret.csv");

        var act = () => new DataSourceCatalog().AddCsvFile("x", escaping, allowedRoot: root);

        act.Should().Throw<UnauthorizedAccessException>();
    }
}
