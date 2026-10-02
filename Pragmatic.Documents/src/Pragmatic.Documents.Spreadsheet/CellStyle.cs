namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// Style properties for a spreadsheet cell. Styles are indexed in XLSX (not inline).
/// </summary>
public sealed record CellStyle
{
    // --- Font ---
    public string? FontFamily { get; init; }
    /// <summary>Font size in points.</summary>
    public double? FontSize { get; init; }
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    /// <summary>Font color as hex (e.g. "FF0000").</summary>
    public string? FontColor { get; init; }

    // --- Fill ---
    /// <summary>Background fill color as hex (e.g. "FFFF00").</summary>
    public string? BackgroundColor { get; init; }

    // --- Alignment ---
    public HorizontalAlign? HorizontalAlign { get; init; }
    public VerticalAlign? VerticalAlign { get; init; }
    public bool? WrapText { get; init; }

    // --- Number format ---
    /// <summary>Number format string (e.g. "#,##0.00", "dd/MM/yyyy"). Always invariant for XLSX.</summary>
    public string? NumberFormat { get; init; }

    // --- Borders ---
    public BorderSide? BorderTop { get; init; }
    public BorderSide? BorderBottom { get; init; }
    public BorderSide? BorderLeft { get; init; }
    public BorderSide? BorderRight { get; init; }
}
