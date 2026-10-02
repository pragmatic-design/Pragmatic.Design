using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Spreadsheet.Tests;

public class SpreadsheetSerializerTests
{
    [Fact]
    public void Roundtrip_PreservesStructureAndStyle()
    {
        var model = new SpreadsheetBuilder()
            .Title("Export")
            .Author("Front Desk")
            .Sheet("Guests", s => s
                .Column(width: 8)
                .Column(width: 20)
                .Freeze(rows: 1, columns: 0)
                .Merge("A1", "B1")
                .HeaderRow("Id", "Name")
                .Row(1.0, "Ada")
                .FormulaRow("=COUNTA(A2:A2)", ""))
            .Build();

        var json = SpreadsheetSerializer.Serialize(model);
        var back = SpreadsheetSerializer.Deserialize(json);

        back.Should().NotBeNull();
        back!.Title.Should().Be("Export");
        back.Sheets.Should().HaveCount(1);

        var sheet = back.Sheets[0];
        sheet.Name.Should().Be("Guests");
        sheet.Columns.Should().HaveCount(2);
        sheet.Columns![0].Width.Should().Be(8);
        sheet.FrozenPane.Should().Be(new FrozenPane(1, 0));
        sheet.MergedCells.Should().ContainSingle().Which.Should().Be(new MergeRange("A1", "B1"));
        sheet.Rows[0].Cells[0].Style!.Bold.Should().BeTrue();
        sheet.Rows[2].Cells[0].Formula.Should().Be("COUNTA(A2:A2)");
    }

    [Theory]
    [MemberData(nameof(CellValueCases))]
    public void Roundtrip_PreservesCellValueClrType(object? value)
    {
        var model = new SpreadsheetBuilder()
            .Sheet("S", s => s.StyledRow(new Cell { Value = value }))
            .Build();

        var back = SpreadsheetSerializer.Deserialize(SpreadsheetSerializer.Serialize(model));

        var roundTripped = back!.Sheets[0].Rows[0].Cells[0].Value;
        if (value is null)
            roundTripped.Should().BeNull();
        else
            roundTripped.Should().BeOfType(value.GetType()).And.Be(value);
    }

    public static IEnumerable<object?[]> CellValueCases() =>
    [
        ["a string"],
        [42.5d],
        [123.4567890123456789m],
        [true],
        [false],
        [new DateTime(2026, 1, 15, 9, 30, 0, DateTimeKind.Utc)],
        [null],
    ];

    [Fact]
    public void Roundtrip_IntegerCellValue_NormalisedToDouble()
    {
        // Row(params object?[]) accepts arbitrary types; XLSX has no integer type so integers
        // round-trip as double (documented behaviour of CellValueJsonConverter).
        var model = new SpreadsheetBuilder()
            .Sheet("S", s => s.Row(7))
            .Build();

        var back = SpreadsheetSerializer.Deserialize(SpreadsheetSerializer.Serialize(model));

        back!.Sheets[0].Rows[0].Cells[0].Value.Should().Be(7.0d).And.BeOfType<double>();
    }

    [Fact]
    public void SerializePretty_ProducesIndentedJson()
    {
        var model = new SpreadsheetBuilder().Title("T").Build();
        SpreadsheetSerializer.SerializePretty(model).Should().Contain("\n");
    }
}
