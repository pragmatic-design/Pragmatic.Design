using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Result.Serialization;

/// <summary>
///     JSON converter for the untyped <see cref="Result{TValue}" /> variant, whose error slot is the
///     abstract <see cref="Error" /> base rather than a concrete typed error.
/// </summary>
/// <remarks>
///     <para>
///         Serializes with the same wire shape as <see cref="ResultJsonConverter{TValue,TError}" />:
///         <code>
/// { "isSuccess": true,  "value": ... }   // success
/// { "isSuccess": false, "error": { ..., "$errorType": "FQN" } }   // failure
/// </code>
///     </para>
///     <para>
///         Because the error is the abstract <see cref="Error" /> base — which
///         <see cref="System.Text.Json" /> cannot instantiate — the concrete type is recovered on read
///         from the <c>"$errorType"</c> discriminator via <see cref="ErrorTypeRegistry" />, falling back
///         GRACEFULLY to <see cref="SerializedError" /> (code, status, title, and extensions preserved)
///         when the discriminator is missing or unregistered. Register custom error types with
///         <see cref="ErrorTypeRegistry.Register{TError}()" />.
///     </para>
///     <para>
///         <b>Success with a null value is allowed</b>, mirroring
///         <see cref="Result{TValue}.Success(TValue)" />: a missing <c>value</c> deserializes to
///         <c>null</c> for reference / nullable types, and is rejected only for a non-nullable value type
///         (<c>default(TValue) is not null</c>).
///     </para>
/// </remarks>
public sealed class UntypedResultJsonConverter<TValue> : JsonConverter<Result<TValue>>
{
    /// <inheritdoc />
    public override Result<TValue> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token");

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        bool? isSuccess = null;
        foreach (var prop in root.EnumerateObject())
            if (string.Equals(prop.Name, "isSuccess", StringComparison.OrdinalIgnoreCase))
            {
                isSuccess = prop.Value.GetBoolean();
                break;
            }

        if (!isSuccess.HasValue)
            throw new JsonException("Missing 'isSuccess' property");

        if (isSuccess.Value)
        {
            if (!TryGetProperty(root, "value", out var valueElement))
            {
                // Result<TValue> accepts null on success; only a non-nullable value type genuinely
                // requires a value to be present.
                if (default(TValue) is not null)
                    throw new JsonException("Missing 'value' property for success result");
                return Result<TValue>.Success(default!);
            }

            return Result<TValue>.Success(valueElement.Deserialize(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<TValue>(options))!);
        }

        if (!TryGetProperty(root, "error", out var errorElement))
            throw new JsonException("Missing 'error' property for failure result");

        // The error slot is the abstract Error base: resolve the concrete type from the $errorType
        // discriminator, falling back to SerializedError (itself an Error) when it cannot be resolved.
        var error = ErrorJsonHelper.ReadError(errorElement, options) as Error
                    ?? errorElement.Deserialize(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<SerializedError>(options))
                    ?? new SerializedError();

        return Result<TValue>.Failure(error);
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        Result<TValue> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("isSuccess", value.IsSuccess);

        if (value.IsSuccess)
        {
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, value.Value, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<TValue>(options));
        }
        else
        {
            writer.WritePropertyName("error");
            ErrorJsonHelper.WriteDeclared(writer, value.Error, options);
        }

        writer.WriteEndObject();
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var prop in root.EnumerateObject())
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }

        value = default;
        return false;
    }

    static UntypedResultJsonConverter() => ErrorTypeRegistry.RegisterDefaults();
}
