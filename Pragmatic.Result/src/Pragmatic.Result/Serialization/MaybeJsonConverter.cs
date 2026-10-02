using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Result.Serialization;

/// <summary>
///     JSON converter for <see cref="Maybe{T}" /> types.
/// </summary>
public sealed class MaybeJsonConverter<T> : JsonConverter<Maybe<T>>
{
    /// <inheritdoc />
    public override Maybe<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return Maybe<T>.None();

        var value = JsonSerializer.Deserialize(ref reader, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<T>(options));
        return value is null ? Maybe<T>.None() : Maybe<T>.Some(value);
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        Maybe<T> value,
        JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Value, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<T>(options));
    }
}