namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// Column definition with optional width (in character units), visibility, and default style.
/// </summary>
public sealed record Column(double? Width = null, bool Hidden = false, CellStyle? Style = null);
