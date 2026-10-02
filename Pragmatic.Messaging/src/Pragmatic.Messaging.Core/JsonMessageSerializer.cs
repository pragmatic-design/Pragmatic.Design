using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Pragmatic.Serialization;

namespace Pragmatic.Messaging;

/// <summary>
///     Default JSON message serializer using System.Text.Json.
///     Options come from the shared <see cref="PragmaticJsonOptions"/> seam: when the host
///     registers source-generated contexts and disables the reflection fallback, this serializer
///     becomes AOT-safe without changes.
/// </summary>
[RequiresUnreferencedCode("JSON serialization may use reflection unless the host registers source-generated contexts via UseJson(...) and disables the reflection fallback.")]
[RequiresDynamicCode("JSON serialization may require dynamic code unless the host registers source-generated contexts via UseJson(...) and disables the reflection fallback.")]
public sealed class JsonMessageSerializer(PragmaticJsonOptions? jsonOptions = null) : IMessageSerializer
{
    // Options come from the shared seam; null (manual wiring / tests) falls back to the shared
    // default (camelCase + reflection).
    private readonly JsonSerializerOptions _options = (jsonOptions ?? PragmaticJsonOptions.Default).Build();

    /// <inheritdoc />
    public string SerializeToString<T>(T message) where T : notnull
        => JsonSerializer.Serialize(message, _options);

    /// <inheritdoc />
    public T? DeserializeFromString<T>(string json) where T : notnull
        => JsonSerializer.Deserialize<T>(json, _options);

    /// <inheritdoc />
    public byte[] Serialize<T>(T message) where T : notnull
        => JsonSerializer.SerializeToUtf8Bytes(message, _options);

    /// <inheritdoc />
    public T? Deserialize<T>(byte[] data) where T : notnull
        => JsonSerializer.Deserialize<T>(data, _options);

    /// <inheritdoc />
    public byte[] Serialize(object message, Type type)
        => JsonSerializer.SerializeToUtf8Bytes(message, type, _options);

    /// <inheritdoc />
    public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
        => JsonSerializer.Deserialize(data.Span, type, _options);

    /// <inheritdoc />
    public ValueTask<object?> DeserializeAsync(Stream data, Type type, CancellationToken ct = default)
        => JsonSerializer.DeserializeAsync(data, type, _options, ct);
}
