using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Spreadsheet.Tests;

public class CellRefTests
{
    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void ColumnToLetter_ConvertsCorrectly(int index, string expected)
    {
        CellRef.ColumnToLetter(index).Should().Be(expected);
    }

    [Theory]
    [InlineData("A", 0)]
    [InlineData("B", 1)]
    [InlineData("Z", 25)]
    [InlineData("AA", 26)]
    [InlineData("AB", 27)]
    [InlineData("AZ", 51)]
    [InlineData("BA", 52)]
    [InlineData("ZZ", 701)]
    [InlineData("AAA", 702)]
    public void LetterToColumn_ConvertsCorrectly(string letters, int expected)
    {
        CellRef.LetterToColumn(letters).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 0, "A1")]
    [InlineData(0, 1, "B1")]
    [InlineData(1, 0, "A2")]
    [InlineData(9, 2, "C10")]
    [InlineData(0, 26, "AA1")]
    public void FromIndex_CreatesA1Reference(int row, int col, string expected)
    {
        CellRef.FromIndex(row, col).Should().Be(expected);
    }

    [Theory]
    [InlineData("A1", 0, 0)]
    [InlineData("B1", 0, 1)]
    [InlineData("A2", 1, 0)]
    [InlineData("C10", 9, 2)]
    [InlineData("AA1", 0, 26)]
    public void ToIndex_ParsesA1Reference(string cellRef, int expectedRow, int expectedCol)
    {
        var (row, col) = CellRef.ToIndex(cellRef);
        row.Should().Be(expectedRow);
        col.Should().Be(expectedCol);
    }

    [Fact]
    public void Roundtrip_ColumnToLetterAndBack()
    {
        for (var i = 0; i < 1000; i++)
        {
            var letters = CellRef.ColumnToLetter(i);
            CellRef.LetterToColumn(letters).Should().Be(i);
        }
    }
}
