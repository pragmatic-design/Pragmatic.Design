using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Result.Serialization;

/// <summary>
///     JSON converter for <see cref="Result{TValue, TError}" /> types.
/// </summary>
/// <remarks>
///     <para>
///         Serializes Result types as JSON with the following structure:
///         <code>
/// // Success case
/// { "isSuccess": true, "value": {...} }
/// 
/// // Failure case
/// { "isSuccess": false, "error": {...} }
/// </code>
///     </para>
///     <para>
///         To use, register in JsonSerializerOptions:
///         <code>
/// options.Converters.Add(new ResultJsonConverter&lt;OrderDto, NotFoundError&gt;());
/// </code>
///     </para>
///     <para>
///         <b>Deserialization of <typeparamref name="TError"/>:</b> when <typeparamref name="TError"/> is a
///         concrete type the error is deserialized directly into it. When it is the <see cref="IError"/>
///         interface or the abstract <see cref="Error"/> base — types <see cref="System.Text.Json"/>
///         cannot instantiate — the converter recovers the concrete type from the
///         <c>"$errorType"</c> discriminator written by <see cref="ErrorJsonHelper"/> and resolved via
///         <see cref="ErrorTypeRegistry"/>. The built-in framework errors are registered out of the box;
///         register custom error types with <see cref="ErrorTypeRegistry.Register{TError}()"/>. An
///         unregistered discriminator falls back GRACEFULLY to <see cref="SerializedError"/> (code,
///         status, title, and extensions preserved) instead of throwing.
///     </para>
///     <para>
///         <b>Missing-value validation:</b> on a success payload a missing <c>value</c> is rejected only
///         when <typeparamref name="TValue"/> is a non-nullable value type (<c>default(TValue) is not
///         null</c> — the only signal observable at runtime). For reference types a missing value
///         deserializes to <c>null</c>, consistent with the lenient <c>TValue → Result</c> implicit
///         conversion; require non-null yourself if your domain needs it.
///     </para>
/// </remarks>
public sealed class ResultJsonConverter<TValue, TError> : JsonConverter<Result<TValue, TError>>
    where TError : IError
{
    /// <inheritdoc />
    public override Result<TValue, TError> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token");

        // Buffer the object so we can read `isSuccess` FIRST and then deserialize ONLY the branch that
        // matters. The previous single-pass reader deserialized whichever of `value`/`error` it
        // encountered regardless of property order — so a payload with `value` before `isSuccess:false`
        // would allocate (and run any custom TValue converter's side-effects) for a value that is then
        // discarded. JsonDocument buffers once; the branch we don't need is never deserialized.
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        bool? isSuccess = null;
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.NameEquals("isSuccess") || string.Equals(prop.Name, "issuccess", StringComparison.OrdinalIgnoreCase))
            {
                isSuccess = prop.Value.GetBoolean();
                break;
            }
        }

        if (!isSuccess.HasValue)
            throw new JsonException("Missing 'isSuccess' property");

        if (isSuccess.Value)
        {
            if (!TryGetProperty(root, "value", out var valueElement))
            {
                if (default(TValue) is not null)
                    throw new JsonException("Missing 'value' property for success result");
                return default(TValue)!;
            }
            return valueElement.Deserialize(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<TValue>(options))!;
        }

        if (!TryGetProperty(root, "error", out var errorElement))
            throw new JsonException("Missing 'error' property for failure result");

        // When TError is a concrete instantiable type, deserialize it directly (preserves the historic
        // behavior and any custom properties). When TError is the IError interface or the abstract Error
        // base, System.Text.Json cannot instantiate it, so resolve the concrete type from the
        // $errorType discriminator via ErrorTypeRegistry, falling back to SerializedError.
        if (s_errorIsConcrete)
        {
            var typed = errorElement.Deserialize(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<TError>(options));
            if (typed is null)
                throw new JsonException("Missing 'error' property for failure result");
            return typed;
        }

        var error = (TError)ErrorJsonHelper.ReadError(errorElement, options);
        return error;
    }

    // IError / abstract Error cannot be instantiated by System.Text.Json — those go through the
    // discriminator + registry path; everything else deserializes directly as TError.
    private static readonly bool s_errorIsConcrete =
        !typeof(TError).IsInterface && !typeof(TError).IsAbstract;

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

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        Result<TValue, TError> value,
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

    static ResultJsonConverter() => ErrorTypeRegistry.RegisterDefaults();
}