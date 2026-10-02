using System.Text.Json.Serialization;

namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// A single cell in a spreadsheet. Value types: string, double, decimal, DateTime, bool, null.
/// </summary>
public sealed record Cell
{
    /// <summary>Cell value: string, double, decimal, DateTime, bool, or null.</summary>
    [JsonConverter(typeof(CellValueJsonConverter))]
    public object? Value { get; init; }

    /// <summary>Formula expression (e.g. "SUM(A1:A10)"). Written as-is to OOXML.</summary>
    public string? Formula { get; init; }

    public CellStyle? Style { get; init; }
}
