using System.Text.Json.Serialization;

namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// Source-generated JSON serializer context for AOT compatibility.
/// </summary>
[JsonSerializable(typeof(SpreadsheetModel))]
[JsonSerializable(typeof(Sheet))]
[JsonSerializable(typeof(Row))]
[JsonSerializable(typeof(Cell))]
[JsonSerializable(typeof(Column))]
[JsonSerializable(typeof(CellStyle))]
[JsonSerializable(typeof(BorderSide))]
[JsonSerializable(typeof(MergeRange))]
[JsonSerializable(typeof(FrozenPane))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
public partial class SpreadsheetJsonContext : JsonSerializerContext;
