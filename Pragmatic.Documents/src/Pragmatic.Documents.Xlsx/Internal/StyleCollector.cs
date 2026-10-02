using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Xlsx.Internal;

/// <summary>
/// Collects unique style components and builds indexed style references for styles.xml.
/// XLSX uses indexed styles: each cell references a cellXf index.
/// </summary>
internal sealed class StyleCollector
{
    private static readonly FontDef DefaultFont = new("Calibri", 11, false, false, null);
    private const string DefaultFontKey = "Calibri|11|False|False|"; // Must match the key built in GetFontIndex

    private readonly List<FontDef> _fonts = [DefaultFont]; // Default font at 0
    private readonly List<FillDef> _fills = [new(null), new(null)]; // 0=none, 1=gray125 (required by Excel)
    private readonly List<BorderDef> _borders = [BorderDef.Empty]; // Default border at 0
    private readonly List<NumFmtDef> _numFmts = [];
    private readonly List<CellXfDef> _cellXfs = [new(0, 0, 0, 0)]; // Default xf at 0
    // Seed the default font so an explicitly-default style returns index 0 instead of
    // appending a second identical FontDef.
    private readonly Dictionary<string, int> _fontIndex = new() { [DefaultFontKey] = 0 };
    private readonly Dictionary<string, int> _fillIndex = [];
    private readonly Dictionary<string, int> _borderIndex = [];
    private readonly Dictionary<string, int> _numFmtIndex = [];
    private readonly Dictionary<string, int> _xfIndex = [];
    private int _nextNumFmtId = 164; // Custom numFmt IDs start at 164

    /// <summary>
    /// Get or create a cellXf index for a cell, applying a default date number-format when the value
    /// is a <see cref="DateTime"/>/<see cref="DateTimeOffset"/> and no explicit format is set — without
    /// it the cell would show a raw Excel serial number (e.g. 46037) instead of a date.
    /// </summary>
    internal int GetStyleIndex(Cell cell) => GetStyleIndex(EffectiveStyle(cell));

    private static CellStyle? EffectiveStyle(Cell cell)
    {
        if (cell.Style?.NumberFormat is not null) return cell.Style; // explicit format wins
        var hasTime = cell.Value switch
        {
            DateTime dt => dt.TimeOfDay != TimeSpan.Zero,
            DateTimeOffset dto => dto.TimeOfDay != TimeSpan.Zero,
            _ => (bool?)null
        };
        if (hasTime is null) return cell.Style; // not a date value
        var format = hasTime.Value ? "yyyy-mm-dd hh:mm:ss" : "yyyy-mm-dd";
        return (cell.Style ?? new CellStyle()) with { NumberFormat = format };
    }

    /// <summary>Get or create a cellXf index for the given CellStyle.</summary>
    internal int GetStyleIndex(CellStyle? style)
    {
        if (style is null) return 0;

        var fontId = GetFontIndex(style);
        var fillId = GetFillIndex(style);
        var borderId = GetBorderIndex(style);
        var numFmtId = GetNumFmtId(style);

        var key = $"{fontId}|{fillId}|{borderId}|{numFmtId}|{(int)(style.HorizontalAlign ?? 0)}|{(int)(style.VerticalAlign ?? 0)}|{(style.WrapText == true ? 1 : 0)}";
        if (_xfIndex.TryGetValue(key, out var idx)) return idx;

        idx = _cellXfs.Count;
        _cellXfs.Add(new CellXfDef(fontId, fillId, borderId, numFmtId)
        {
            HorizontalAlign = style.HorizontalAlign,
            VerticalAlign = style.VerticalAlign,
            WrapText = style.WrapText == true
        });
        _xfIndex[key] = idx;
        return idx;
    }

    private int GetFontIndex(CellStyle style)
    {
        var family = style.FontFamily ?? "Calibri";
        var size = style.FontSize ?? 11;
        var bold = style.Bold == true;
        var italic = style.Italic == true;
        var color = style.FontColor;

        var key = $"{family}|{size}|{bold}|{italic}|{color}";
        // The default font is pre-seeded in _fontIndex (key DefaultFontKey → 0), so an
        // equal font — including the explicit default — returns the existing index here
        // rather than appending a duplicate FontDef.
        if (_fontIndex.TryGetValue(key, out var idx)) return idx;

        idx = _fonts.Count;
        _fonts.Add(new FontDef(family, size, bold, italic, color));
        _fontIndex[key] = idx;
        return idx;
    }

    private int GetFillIndex(CellStyle style)
    {
        if (style.BackgroundColor is null) return 0;

        var key = style.BackgroundColor;
        if (_fillIndex.TryGetValue(key, out var idx)) return idx;

        idx = _fills.Count;
        _fills.Add(new FillDef(style.BackgroundColor));
        _fillIndex[key] = idx;
        return idx;
    }

    private int GetBorderIndex(CellStyle style)
    {
        if (style.BorderTop is null && style.BorderBottom is null && style.BorderLeft is null && style.BorderRight is null)
            return 0;

        var key = $"{FormatBorder(style.BorderTop)}|{FormatBorder(style.BorderBottom)}|{FormatBorder(style.BorderLeft)}|{FormatBorder(style.BorderRight)}";
        if (_borderIndex.TryGetValue(key, out var idx)) return idx;

        idx = _borders.Count;
        _borders.Add(new BorderDef(style.BorderLeft, style.BorderRight, style.BorderTop, style.BorderBottom));
        _borderIndex[key] = idx;
        return idx;
    }

    private int GetNumFmtId(CellStyle style)
    {
        if (style.NumberFormat is null) return 0;

        if (_numFmtIndex.TryGetValue(style.NumberFormat, out var id)) return id;

        id = _nextNumFmtId++;
        _numFmts.Add(new NumFmtDef(id, style.NumberFormat));
        _numFmtIndex[style.NumberFormat] = id;
        return id;
    }

    private static string FormatBorder(BorderSide? side)
        => side is null ? "" : $"{side.Style}:{side.Color}";

    // Accessors for the writer
    internal IReadOnlyList<FontDef> Fonts => _fonts;
    internal IReadOnlyList<FillDef> Fills => _fills;
    internal IReadOnlyList<BorderDef> Borders => _borders;
    internal IReadOnlyList<NumFmtDef> NumFmts => _numFmts;
    internal IReadOnlyList<CellXfDef> CellXfs => _cellXfs;

    // Internal records
    internal sealed record FontDef(string Family, double Size, bool Bold, bool Italic, string? Color);
    internal sealed record FillDef(string? BackgroundColor);
    internal sealed record NumFmtDef(int Id, string FormatCode);

    internal sealed record BorderDef(BorderSide? Left, BorderSide? Right, BorderSide? Top, BorderSide? Bottom)
    {
        internal static BorderDef Empty { get; } = new(null, null, null, null);
    }

    internal sealed record CellXfDef(int FontId, int FillId, int BorderId, int NumFmtId)
    {
        internal HorizontalAlign? HorizontalAlign { get; init; }
        internal VerticalAlign? VerticalAlign { get; init; }
        internal bool WrapText { get; init; }
    }
}
