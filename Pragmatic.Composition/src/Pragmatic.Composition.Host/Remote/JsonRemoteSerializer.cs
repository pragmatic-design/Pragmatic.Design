using System.Text.Json;

namespace Pragmatic.Composition.Remote;

/// <summary>
///     Default JSON implementation of <see cref="IRemoteSerializer"/>.
///     Uses System.Text.Json with camelCase naming and enum-as-string.
/// </summary>
public sealed class JsonRemoteSerializer : IRemoteSerializer
{
    /// <summary>
    ///     Shared singleton for use when no DI container is available (e.g. static call sites or
    ///     generated code). It uses the fixed <see cref="Options"/> below — camelCase, case-insensitive,
    ///     ignore-null. Only use <see cref="Instance"/> when those options are acceptable; when you need
    ///     custom serialization options, register and inject an <see cref="IRemoteSerializer"/> via DI
    ///     instead of reaching for this field.
    /// </summary>
    public static readonly JsonRemoteSerializer Instance = new();

    /// <remarks>
    ///     The resolver comes from the shared seam, so the types crossing a boundary are the ones the
    ///     generated contexts cover. Left to its own devices a fresh <see cref="JsonSerializerOptions" />
    ///     resolves by reflection, which works right up until the app is published Native AOT.
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = global::Pragmatic.Serialization.PragmaticJsonOptions.Default.Build().TypeInfoResolver,
    };

    /// <inheritdoc />
    public string ContentType => "application/json";

    /// <inheritdoc />
    public byte[] Serialize<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<T>(Options));

    /// <inheritdoc />
    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
        => await JsonSerializer
            .DeserializeAsync(stream, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<T>(Options), ct)
            .ConfigureAwait(false);
}
