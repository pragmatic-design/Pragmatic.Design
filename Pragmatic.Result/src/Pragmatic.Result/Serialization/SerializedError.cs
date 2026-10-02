using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Result.Serialization;

/// <summary>
///     A concrete <see cref="Error"/> used as a graceful fallback when a serialized error carries a
///     discriminator that is missing or not registered in <see cref="ErrorTypeRegistry"/>.
/// </summary>
/// <remarks>
///     <para>
///         When <see cref="ResultJsonConverter{TValue,TError}"/> or
///         <see cref="VoidResultJsonConverter{TError}"/> cannot resolve a concrete error type, it
///         deserializes into this carrier rather than throwing. The carrier preserves the error's
///         <see cref="Code"/>, <see cref="StatusCode"/>, and <see cref="Title"/>, plus every additional
///         (extension) JSON property in <see cref="Extensions"/>, so no information is lost on the wire.
///     </para>
///     <para>
///         An error that round-trips through this carrier loses its original CLR type identity — callers
///         that need the concrete type back must register it with <see cref="ErrorTypeRegistry"/>.
///     </para>
/// </remarks>
public sealed record SerializedError : Error
{
    // Code/StatusCode/Title override get-only base members, so they are set through the constructor
    // (STJ uses the [JsonConstructor] below). No explicit [JsonPropertyName]: the writer serialized the
    // original error with the caller's JsonSerializerOptions (incl. any naming policy) and the fallback
    // read uses the SAME options, so constructor-parameter ↔ property matching stays symmetric.

    /// <inheritdoc />
    public override string Code { get; }

    /// <inheritdoc />
    public override int StatusCode { get; }

    /// <inheritdoc />
    public override string Title { get; }

    /// <summary>Creates a fallback carrier. All parameters are optional so the type is also usable via an object initializer.</summary>
    [JsonConstructor]
    public SerializedError(string? code = null, int statusCode = 500, string? title = null)
    {
        Code = string.IsNullOrEmpty(code) ? "UNKNOWN" : code;
        StatusCode = statusCode;
        Title = title ?? string.Empty;
    }

    /// <summary>
    ///     The discriminator that could not be resolved, if one was present on the wire.
    /// </summary>
    [JsonPropertyName(ErrorTypeRegistry.DiscriminatorProperty)]
    public string? OriginalErrorType { get; init; }

    /// <summary>
    ///     Any additional (extension) properties captured from the serialized error that are not part of
    ///     the base error shape.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extensions { get; init; }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (Extensions is null)
            return;

        foreach (var pair in Extensions)
            extensions[pair.Key] = pair.Value;
    }
}
