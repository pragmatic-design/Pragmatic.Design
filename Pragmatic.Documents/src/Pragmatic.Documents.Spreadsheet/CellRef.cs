namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// Utility for converting between A1-style cell references and zero-based row/column indices.
/// </summary>
public static class CellRef
{
    /// <summary>Convert zero-based column index to letter(s): 0→A, 25→Z, 26→AA.</summary>
    public static string ColumnToLetter(int column)
    {
        var result = "";
        var col = column;
        while (col >= 0)
        {
            result = (char)('A' + col % 26) + result;
            col = col / 26 - 1;
        }
        return result;
    }

    /// <summary>Convert column letter(s) to zero-based index: A→0, Z→25, AA→26.</summary>
    public static int LetterToColumn(string letters)
    {
        var result = 0;
        foreach (var c in letters.ToUpperInvariant())
        {
            result = result * 26 + (c - 'A' + 1);
        }
        return result - 1;
    }

    /// <summary>Create A1-style reference from zero-based row and column: (0,0)→"A1".</summary>
    public static string FromIndex(int row, int column) => $"{ColumnToLetter(column)}{row + 1}";

    /// <summary>Parse A1-style reference to zero-based (row, column): "A1"→(0,0).</summary>
    public static (int Row, int Column) ToIndex(string cellRef)
    {
        var i = 0;
        while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++;
        var letters = cellRef[..i];
        var rowPart = cellRef[i..];
        if (i == 0 || !int.TryParse(rowPart, out var number) || number < 1)
            throw new FormatException($"Invalid A1 cell reference '{cellRef}'.");
        return (number - 1, LetterToColumn(letters));
    }
}
