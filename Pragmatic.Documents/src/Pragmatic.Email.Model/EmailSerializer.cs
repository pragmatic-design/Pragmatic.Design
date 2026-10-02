using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Email.Model;

/// <summary>
/// JSON serialization for Email Models.
/// Uses source-generated <see cref="EmailJsonContext"/> for AOT compatibility.
/// </summary>
public static class EmailSerializer
{
    /// <summary>Serialize an EmailModel to JSON.</summary>
    public static string Serialize(EmailModel model)
        => JsonSerializer.Serialize(model, EmailJsonContext.Default.EmailModel);

    private static readonly JsonSerializerOptions PrettyOptions = new(EmailJsonContext.Default.Options) { WriteIndented = true };
    private static readonly JsonTypeInfo<EmailModel> PrettyTypeInfo =
        (JsonTypeInfo<EmailModel>)PrettyOptions.GetTypeInfo(typeof(EmailModel));

    /// <summary>Serialize an EmailModel to JSON with indentation.</summary>
    public static string SerializePretty(EmailModel model)
        => JsonSerializer.Serialize(model, PrettyTypeInfo);

    /// <summary>Deserialize an EmailModel from JSON.</summary>
    public static EmailModel? Deserialize(string json)
        => JsonSerializer.Deserialize(json, EmailJsonContext.Default.EmailModel);

    /// <summary>Serialize to UTF-8 bytes.</summary>
    public static byte[] SerializeToUtf8(EmailModel model)
        => JsonSerializer.SerializeToUtf8Bytes(model, EmailJsonContext.Default.EmailModel);

    /// <summary>Deserialize from UTF-8 bytes.</summary>
    public static EmailModel? DeserializeFromUtf8(ReadOnlySpan<byte> utf8Json)
        => JsonSerializer.Deserialize(utf8Json, EmailJsonContext.Default.EmailModel);
}
