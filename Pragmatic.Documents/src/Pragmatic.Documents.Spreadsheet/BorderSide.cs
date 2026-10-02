namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// A single border edge with color and line style.
/// </summary>
public sealed record BorderSide(string Color = "000000", BorderLineStyle Style = BorderLineStyle.Thin);
