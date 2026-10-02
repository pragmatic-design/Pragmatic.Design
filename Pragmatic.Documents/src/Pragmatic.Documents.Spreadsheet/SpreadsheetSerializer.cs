using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// JSON serialization for Spreadsheet Models.
/// Uses source-generated <see cref="SpreadsheetJsonContext"/> for AOT compatibility.
/// Weakly-typed cell values round-trip faithfully via <see cref="CellValueJsonConverter"/>.
/// </summary>
public static class SpreadsheetSerializer
{
    // Cached once: allocating a fresh JsonSerializerOptions per SerializePretty call is expensive
    // (each new instance re-resolves and re-caches type metadata on first use).
    private static readonly JsonTypeInfo<SpreadsheetModel> PrettyTypeInfo =
        new JsonSerializerOptions(SpreadsheetJsonContext.Default.Options) { WriteIndented = true }
            .GetTypeInfo(typeof(SpreadsheetModel)) as JsonTypeInfo<SpreadsheetModel>
        ?? throw new InvalidOperationException("Failed to get type info for SpreadsheetModel");

    /// <summary>Serialize a SpreadsheetModel to JSON.</summary>
    public static string Serialize(SpreadsheetModel model)
        => JsonSerializer.Serialize(model, SpreadsheetJsonContext.Default.SpreadsheetModel);

    /// <summary>Serialize a SpreadsheetModel to JSON with indentation.</summary>
    public static string SerializePretty(SpreadsheetModel model)
        => JsonSerializer.Serialize(model, PrettyTypeInfo);

    /// <summary>Deserialize a SpreadsheetModel from JSON.</summary>
    public static SpreadsheetModel? Deserialize(string json)
        => JsonSerializer.Deserialize(json, SpreadsheetJsonContext.Default.SpreadsheetModel);

    /// <summary>Serialize to UTF-8 bytes.</summary>
    public static byte[] SerializeToUtf8(SpreadsheetModel model)
        => JsonSerializer.SerializeToUtf8Bytes(model, SpreadsheetJsonContext.Default.SpreadsheetModel);

    /// <summary>Deserialize from UTF-8 bytes.</summary>
    public static SpreadsheetModel? DeserializeFromUtf8(ReadOnlySpan<byte> utf8Json)
        => JsonSerializer.Deserialize(utf8Json, SpreadsheetJsonContext.Default.SpreadsheetModel);

    /// <summary>Get the shared serializer options (for custom scenarios).</summary>
    public static JsonSerializerOptions GetOptions() => SpreadsheetJsonContext.Default.Options;
}
