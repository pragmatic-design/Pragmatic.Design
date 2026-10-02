using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Documents.Model;

/// <summary>
/// JSON serialization for Document Models.
/// Uses source-generated <see cref="DocumentJsonContext"/> for AOT compatibility.
/// </summary>
public static class DocumentSerializer
{
    // Cached once: allocating a fresh JsonSerializerOptions per SerializePretty call is expensive
    // (each new instance re-resolves and re-caches type metadata on first use).
    private static readonly JsonTypeInfo<DocumentModel> PrettyTypeInfo =
        new JsonSerializerOptions(DocumentJsonContext.Default.Options) { WriteIndented = true }
            .GetTypeInfo(typeof(DocumentModel)) as JsonTypeInfo<DocumentModel>
        ?? throw new InvalidOperationException("Failed to get type info for DocumentModel");

    /// <summary>Serialize a DocumentModel to JSON.</summary>
    public static string Serialize(DocumentModel model)
        => JsonSerializer.Serialize(model, DocumentJsonContext.Default.DocumentModel);

    /// <summary>Serialize a DocumentModel to JSON with indentation.</summary>
    public static string SerializePretty(DocumentModel model)
        => JsonSerializer.Serialize(model, PrettyTypeInfo);

    /// <summary>Deserialize a DocumentModel from JSON.</summary>
    public static DocumentModel? Deserialize(string json)
        => JsonSerializer.Deserialize(json, DocumentJsonContext.Default.DocumentModel);

    /// <summary>Serialize to UTF-8 bytes.</summary>
    public static byte[] SerializeToUtf8(DocumentModel model)
        => JsonSerializer.SerializeToUtf8Bytes(model, DocumentJsonContext.Default.DocumentModel);

    /// <summary>Deserialize from UTF-8 bytes.</summary>
    public static DocumentModel? DeserializeFromUtf8(ReadOnlySpan<byte> utf8Json)
        => JsonSerializer.Deserialize(utf8Json, DocumentJsonContext.Default.DocumentModel);

    /// <summary>Get the shared serializer options (for custom scenarios).</summary>
    public static JsonSerializerOptions GetOptions() => DocumentJsonContext.Default.Options;
}
