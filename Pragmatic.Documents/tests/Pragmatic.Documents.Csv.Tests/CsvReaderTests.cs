using System.Globalization;
using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Csv.Tests;

public class CsvReaderTests
{
    [Fact]
    public void Read_SimpleCSV_ParsesHeadersAndRows()
    {
        var csv = "Name,Age\r\nAlice,30\r\nBob,25";
        var (headers, rows) = ReadString(csv);

        headers.Should().Equal("Name", "Age");
        rows.Should().HaveCount(2);
        rows[0].Should().Equal("Alice", "30");
        rows[1].Should().Equal("Bob", "25");
    }

    [Fact]
    public void Read_QuotedFields_ParsesCorrectly()
    {
        var csv = "Name,Description\r\n\"Alice\",\"Item, with comma\"";
        var (headers, rows) = ReadString(csv);

        rows[0][1].Should().Be("Item, with comma");
    }

    [Fact]
    public void Read_EscapedQuotes()
    {
        var csv = "Text\r\n\"She said \"\"hello\"\"\"";
        var (headers, rows) = ReadString(csv);

        rows[0][0].Should().Be("She said \"hello\"");
    }

    [Fact]
    public void Read_NewlineInField()
    {
        var csv = "Text\r\n\"Line1\nLine2\"";
        var (headers, rows) = ReadString(csv);

        rows[0][0].Should().Be("Line1\nLine2");
    }

    [Fact]
    public void Read_NoHeaders()
    {
        var csv = "A,B\r\nC,D";
        var options = new CsvOptions { HasHeaders = false };
        var (headers, rows) = ReadString(csv, options);

        headers.Should().BeEmpty();
        rows.Should().HaveCount(2);
        rows[0].Should().Equal("A", "B");
    }

    [Fact]
    public void Read_SemicolonDelimiter()
    {
        var csv = "A;B\r\n1;2";
        var options = new CsvOptions { Delimiter = ';' };
        var (headers, rows) = ReadString(csv, options);

        headers.Should().Equal("A", "B");
        rows[0].Should().Equal("1", "2");
    }

    [Fact]
    public void Read_EmptyFields()
    {
        var csv = "A,B,C\r\n,val,\r\n,,";
        var (headers, rows) = ReadString(csv);

        rows[0].Should().Equal("", "val", "");
        rows[1].Should().Equal("", "", "");
    }

    [Fact]
    public void Read_LFLineEndings()
    {
        var csv = "A,B\n1,2\n3,4";
        var (headers, rows) = ReadString(csv);

        headers.Should().Equal("A", "B");
        rows.Should().HaveCount(2);
    }

    [Fact]
    public void Read_FromByteArray()
    {
        var bytes = Encoding.UTF8.GetBytes("X,Y\r\n1,2");
        var (headers, rows) = CsvReader.Read(bytes);

        headers.Should().Equal("X", "Y");
        rows[0].Should().Equal("1", "2");
    }

    [Fact]
    public void ReadAsModel_CreatesSpreadsheetModel()
    {
        var csv = "Name,Total\r\nWidget,99.5";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var model = CsvReader.ReadAsModel(ms, "Import");

        model.Sheets.Should().HaveCount(1);
        model.Sheets[0].Name.Should().Be("Import");
        model.Sheets[0].Rows.Should().HaveCount(2);

        // Header row
        model.Sheets[0].Rows[0].Cells[0].Value.Should().Be("Name");
        model.Sheets[0].Rows[0].Cells[0].Style!.Bold.Should().BeTrue();

        // Data row — value parsed as typed
        model.Sheets[0].Rows[1].Cells[0].Value.Should().Be("Widget");
        model.Sheets[0].Rows[1].Cells[1].Value.Should().Be(99.5m);
    }

    [Fact]
    public void ParseValue_Integer()
    {
        CsvReader.ParseValue("42", CsvOptions.Default).Should().Be(42);
    }

    [Fact]
    public void ParseValue_Decimal()
    {
        CsvReader.ParseValue("1234.56", CsvOptions.Default).Should().Be(1234.56m);
    }

    [Fact]
    public void ParseValue_ItalianDecimal()
    {
        var opts = CsvOptions.Italian;
        CsvReader.ParseValue("1.234,56", opts).Should().Be(1234.56m);
    }

    [Fact]
    public void ParseValue_Date()
    {
        var opts = CsvOptions.Italian;
        var result = CsvReader.ParseValue("10/04/2026", opts);
        result.Should().BeOfType<DateTime>();
        ((DateTime)result!).Should().Be(new DateTime(2026, 4, 10));
    }

    [Fact]
    public void ParseValue_Boolean()
    {
        CsvReader.ParseValue("true", CsvOptions.Default).Should().Be(true);
        CsvReader.ParseValue("FALSE", CsvOptions.Default).Should().Be(false);
    }

    [Fact]
    public void ParseValue_Null_ReturnsNull()
    {
        CsvReader.ParseValue(null, CsvOptions.Default).Should().BeNull();
        CsvReader.ParseValue("", CsvOptions.Default).Should().BeNull();
    }

    [Fact]
    public void ParseValue_PlainString()
    {
        CsvReader.ParseValue("hello world", CsvOptions.Default).Should().Be("hello world");
    }

    [Fact]
    public void Roundtrip_WriteAndRead()
    {
        string[] headers = ["Name", "Total", "Active"];
        List<IReadOnlyList<string?>> rows =
        [
            (string?[])["Widget", "99.50", "true"],
            (string?[])["Gadget", "49.00", "false"]
        ];

        var bytes = CsvWriter.WriteToArray(headers, rows);
        var (readHeaders, readRows) = CsvReader.Read(bytes);

        readHeaders.Should().Equal(headers);
        readRows.Should().HaveCount(2);
        readRows[0].Should().Equal("Widget", "99.50", "true");
    }

    [Fact]
    public void Roundtrip_WithSpecialChars()
    {
        string[] headers = ["Text"];
        List<IReadOnlyList<string?>> rows =
        [
            (string?[])["She said \"hello\""],
            (string?[])["Line1\nLine2"],
            (string?[])["Item, with comma"]
        ];

        var bytes = CsvWriter.WriteToArray(headers, rows);
        var (_, readRows) = CsvReader.Read(bytes);

        readRows[0][0].Should().Be("She said \"hello\"");
        readRows[1][0].Should().Be("Line1\nLine2");
        readRows[2][0].Should().Be("Item, with comma");
    }

    [Fact]
    public void Roundtrip_Italian()
    {
        var options = CsvOptions.Italian;
        var sheet = new Sheet
        {
            Name = "Data",
            Rows =
            [
                new Row([new Cell { Value = "Prodotto" }, new Cell { Value = "Totale" }]),
                new Row([new Cell { Value = "Widget" }, new Cell { Value = 1234.56 }])
            ]
        };

        using var ms = new MemoryStream();
        CsvWriter.Write(ms, sheet, options);
        ms.Position = 0;

        var csv = Encoding.UTF8.GetString(ms.ToArray());
        csv.Should().Contain("Prodotto;Totale");
        csv.Should().Contain("Widget;1234,56");
    }

    private static (string[] Headers, List<string[]> Rows) ReadString(string csv, CsvOptions? options = null)
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return CsvReader.Read(ms, options);
    }
    [Fact]
    public void ReadRows_Streaming_YieldsAllRowsIncludingHeader()
    {
        var csv = "Name,Age\r\nAlice,30\r\nBob,25";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var rows = CsvReader.ReadRows(ms).ToList();

        rows.Should().HaveCount(3);
        rows[0].Should().Equal("Name", "Age");
        rows[2].Should().Equal("Bob", "25");
    }

    [Fact]
    public void ReadRows_Streaming_IsLazy()
    {
        var csv = string.Join("\n", Enumerable.Range(0, 1000).Select(i => $"row{i},x"));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        // Taking only the first rows must not consume the whole stream.
        var first = CsvReader.ReadRows(ms).Take(2).ToList();

        first.Should().HaveCount(2);
        ms.Position.Should().BeLessThan(ms.Length, "enumeration stops reading once the caller stops");
    }

    [Fact]
    public void Read_RowCountBeyondMaxRows_Throws()
    {
        var csv = "a\nb\nc\nd";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var act = () => CsvReader.Read(ms, new CsvOptions { MaxRows = 3 });

        act.Should().Throw<FormatException>().WithMessage("*MaxRows*");
    }

    [Fact]
    public void Read_FieldBeyondMaxFieldChars_Throws()
    {
        var csv = "small," + new string('x', 100);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var act = () => CsvReader.Read(ms, new CsvOptions { MaxFieldChars = 50 });

        act.Should().Throw<FormatException>().WithMessage("*MaxFieldChars*");
    }
}
