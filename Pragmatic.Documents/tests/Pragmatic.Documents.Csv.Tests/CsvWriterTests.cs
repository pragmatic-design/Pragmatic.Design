using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Csv.Tests;

public class CsvWriterTests
{
    [Fact]
    public void Write_SimpleHeadersAndRows()
    {
        string[] headers = ["Name", "Age"];
        List<IReadOnlyList<string?>> rows =
        [
            (string?[])["Alice", "30"],
            (string?[])["Bob", "25"]
        ];

        var csv = GetString(s => CsvWriter.Write(s, headers, rows));

        csv.Should().Contain("Name,Age");
        csv.Should().Contain("Alice,30");
        csv.Should().Contain("Bob,25");
    }

    [Fact]
    public void Write_EscapesCommasInFields()
    {
        string[] headers = ["Description"];
        List<IReadOnlyList<string?>> rows = [(string?[])["Item, with comma"]];

        var csv = GetString(s => CsvWriter.Write(s, headers, rows));

        csv.Should().Contain("\"Item, with comma\"");
    }

    [Fact]
    public void Write_EscapesQuotesInFields()
    {
        string[] headers = ["Name"];
        List<IReadOnlyList<string?>> rows = [(string?[])["She said \"hello\""]];

        var csv = GetString(s => CsvWriter.Write(s, headers, rows));

        csv.Should().Contain("\"She said \"\"hello\"\"\"");
    }

    [Fact]
    public void Write_EscapesNewlinesInFields()
    {
        string[] headers = ["Text"];
        List<IReadOnlyList<string?>> rows = [(string?[])["Line1\nLine2"]];

        var csv = GetString(s => CsvWriter.Write(s, headers, rows));

        csv.Should().Contain("\"Line1\nLine2\"");
    }

    [Fact]
    public void Write_NullValues_BecomeEmpty()
    {
        string[] headers = ["A", "B"];
        List<IReadOnlyList<string?>> rows = [(string?[])[null, "val"]];

        var csv = GetString(s => CsvWriter.Write(s, headers, rows));

        csv.Should().Contain(",val");
    }

    [Fact]
    public void Write_SemicolonDelimiter()
    {
        var options = new CsvOptions { Delimiter = ';' };
        string[] headers = ["A", "B"];
        List<IReadOnlyList<string?>> rows = [(string?[])["1", "2"]];

        var csv = GetString(s => CsvWriter.Write(s, headers, rows, options));

        csv.Should().Contain("A;B");
        csv.Should().Contain("1;2");
    }

    [Fact]
    public void Write_FromSheet_FormatsValues()
    {
        var sheet = new Sheet
        {
            Name = "Data",
            Rows =
            [
                new Row([new Cell { Value = "Name" }, new Cell { Value = "Total" }]),
                new Row([new Cell { Value = "Widget" }, new Cell { Value = 99.5 }])
            ]
        };

        var csv = GetString(s => CsvWriter.Write(s, sheet));

        csv.Should().Contain("Name,Total");
        csv.Should().Contain("Widget,99.5");
    }

    [Fact]
    public void Write_ItalianCulture_FormatsNumbers()
    {
        var options = CsvOptions.Italian;
        var sheet = new Sheet
        {
            Name = "Data",
            Rows = [new Row([new Cell { Value = 1234.56 }])]
        };

        var csv = GetString(s => CsvWriter.Write(s, sheet, options));

        csv.Should().Contain("1234,56");
    }

    [Fact]
    public void Write_DateTime_UsesFormat()
    {
        var options = CsvOptions.Italian;
        var date = new DateTime(2026, 4, 10);
        var sheet = new Sheet
        {
            Name = "Data",
            Rows = [new Row([new Cell { Value = date }])]
        };

        var csv = GetString(s => CsvWriter.Write(s, sheet, options));

        csv.Should().Contain("10/04/2026");
    }

    [Fact]
    public void WriteToArray_ProducesBytes()
    {
        string[] headers = ["X"];
        List<IReadOnlyList<string?>> rows = [(string?[])["1"]];

        var bytes = CsvWriter.WriteToArray(headers, rows);

        bytes.Should().NotBeEmpty();
        Encoding.UTF8.GetString(bytes).Should().Contain("X");
    }

    [Fact]
    public void Write_EmptyModel_NoOutput()
    {
        var model = new SpreadsheetModel();
        var csv = GetString(s => CsvWriter.Write(s, model));
        csv.Should().BeEmpty();
    }

    [Fact]
    public void EscapeField_NoSpecialChars_Unchanged()
    {
        CsvWriter.EscapeField("hello", ',').Should().Be("hello");
    }

    [Fact]
    public void EscapeField_EmptyString_Unchanged()
    {
        CsvWriter.EscapeField("", ',').Should().Be("");
    }

    [Fact]
    public void FormatValue_Boolean()
    {
        CsvWriter.FormatValue(true, CsvOptions.Default).Should().Be("true");
        CsvWriter.FormatValue(false, CsvOptions.Default).Should().Be("false");
    }

    [Fact]
    public void FormatValue_Null()
    {
        CsvWriter.FormatValue(null, CsvOptions.Default).Should().Be("");
    }

    [Fact]
    public void EscapeField_FormulaInjection_PrefixesWithTab()
    {
        CsvWriter.EscapeField("=HYPERLINK(\"http://evil\")", ',').Should().StartWith("\"\t=");
        CsvWriter.EscapeField("+cmd|'/C calc'", ',').Should().StartWith("\"\t+");
        CsvWriter.EscapeField("-1+1", ',').Should().StartWith("\"\t-");
        CsvWriter.EscapeField("@SUM(A1)", ',').Should().StartWith("\"\t@");
        // OWASP lead-in set also includes a leading tab / carriage-return.
        CsvWriter.EscapeField("\t=SUM(A1)", ',').Should().StartWith("\"\t\t");
        CsvWriter.EscapeField("\r=SUM(A1)", ',').Should().StartWith("\"\t\r");
    }

    [Fact]
    public void EscapeField_FormulaProtectionDisabled_NoPrefix()
    {
        CsvWriter.EscapeField("=SUM(A1)", ',', formulaProtection: false).Should().Be("=SUM(A1)");
    }

    [Fact]
    public void Write_FormulaInjection_ProtectedByDefault()
    {
        string[] headers = ["Name"];
        List<IReadOnlyList<string?>> rows = [(string?[])["=HYPERLINK(\"http://evil\")"]];

        var csv = GetString(s => CsvWriter.Write(s, headers, rows));

        csv.Should().Contain("\t=HYPERLINK");
        csv.Should().NotContain("\nName\n"); // header not prefixed
    }

    private static string GetString(Action<Stream> write)
    {
        using var ms = new MemoryStream();
        write(ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
