using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Result.Serialization;

/// <summary>
///     JSON converter for <see cref="VoidResult{TError}" /> types.
/// </summary>
/// <remarks>
///     When <typeparamref name="TError"/> is a concrete type the error is deserialized directly into it.
///     When it is the <see cref="IError"/> interface or the abstract <see cref="Error"/> base, the
///     converter recovers the concrete type from the <c>"$errorType"</c> discriminator resolved via
///     <see cref="ErrorTypeRegistry"/>, falling back GRACEFULLY to <see cref="SerializedError"/> when the
///     discriminator is missing or unregistered. Register custom error types with
///     <see cref="ErrorTypeRegistry.Register{TError}()"/>.
/// </remarks>
public sealed class VoidResultJsonConverter<TError> : JsonConverter<VoidResult<TError>>
    where TError : IError
{
    /// <inheritdoc />
    public override VoidResult<TError> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token");

        // Buffer the object so the discriminator can be read before deserializing the error branch.
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        bool? isSuccess = null;
        JsonElement errorElement = default;
        var hasError = false;

        foreach (var prop in root.EnumerateObject())
            if (string.Equals(prop.Name, "isSuccess", StringComparison.OrdinalIgnoreCase))
                isSuccess = prop.Value.GetBoolean();
            else if (string.Equals(prop.Name, "error", StringComparison.OrdinalIgnoreCase))
            {
                errorElement = prop.Value;
                hasError = true;
            }

        if (!isSuccess.HasValue)
            throw new JsonException("Missing 'isSuccess' property");

        if (isSuccess.Value)
            return VoidResult<TError>.Success();

        if (!hasError)
            throw new JsonException("Missing 'error' property for failure result");

        // Concrete TError deserializes directly (historic behavior); IError / abstract Error is resolved
        // from the $errorType discriminator via ErrorTypeRegistry, falling back to SerializedError.
        if (s_errorIsConcrete)
        {
            var typed = errorElement.Deserialize(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<TError>(options));
            if (typed is null)
                throw new JsonException("Missing 'error' property for failure result");
            return typed;
        }

        return (TError)ErrorJsonHelper.ReadError(errorElement, options);
    }

    // IError / abstract Error cannot be instantiated by System.Text.Json — those go through the
    // discriminator + registry path; everything else deserializes directly as TError.
    private static readonly bool s_errorIsConcrete =
        !typeof(TError).IsInterface && !typeof(TError).IsAbstract;

    static VoidResultJsonConverter() => ErrorTypeRegistry.RegisterDefaults();

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        VoidResult<TError> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("isSuccess", value.IsSuccess);

        if (!value.IsSuccess)
        {
            writer.WritePropertyName("error");
            ErrorJsonHelper.WriteDeclared(writer, value.Error, options);
        }

        writer.WriteEndObject();
    }
}