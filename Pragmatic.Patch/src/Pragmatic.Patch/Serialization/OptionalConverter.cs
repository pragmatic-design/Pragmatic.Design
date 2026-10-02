// =============================================================================
// Pragmatic.Patch - OptionalConverter<T>
// Typed System.Text.Json converter for Optional<T>
// =============================================================================

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Patch.Serialization;

/// <summary>
///     Typed JSON converter for <see cref="Optional{T}" />.
///     Registered per <typeparamref name="T" />: there is no open-generic factory.
/// </summary>
/// <remarks>
///     Serialization behavior:
///     <list type="bullet">
///         <item><description><b>Undefined</b>: should be skipped by the caller (e.g. <c>DefaultIgnoreCondition</c>).
///         If Write is called for an undefined value, it writes <c>null</c> as a safe fallback.</description></item>
///         <item><description><b>Null</b>: writes JSON <c>null</c>.</description></item>
///         <item><description><b>Value</b>: writes the serialized value.</description></item>
///     </list>
///     The SG-generated converter handles Undefined correctly by skipping the property entirely.
///     This runtime converter cannot skip the property (that's the caller's responsibility).
/// </remarks>
[RequiresDynamicCode("JSON serialization of Optional<T> may require runtime code generation. For NativeAOT, use the SG-generated converter.")]
[RequiresUnreferencedCode("JSON serialization of Optional<T> may require types that cannot be statically analyzed. For NativeAOT, use the SG-generated converter.")]
public sealed class OptionalConverter<T> : JsonConverter<Optional<T>>
{
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // JSON null is mapped to the Null state for every T, including non-nullable value types. This
        // converter reads a single *value* and has no notion of an absent property — "undefined" is not a
        // state it can observe, so it cannot distinguish `{"n": null}` from `{}`. The SG-generated converter
        // reads the whole object and does make that distinction, mapping null on a non-nullable value type
        // to Undefined. The consequence, and how to model a clearable value type, are covered in
        // docs/tri-state-semantics.md § "Value types and explicit null".
        if (reader.TokenType == JsonTokenType.Null)
            return Optional<T>.Null;

        var value = JsonSerializer.Deserialize<T>(ref reader, options);
        return Optional<T>.Of(value);
    }

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        // Undefined should ideally never reach Write — the SG converter skips undefined properties.
        // For the runtime fallback, null is the safest output (JSON spec has no "missing" token).
        if (value.IsUndefined)
        {
            writer.WriteNullValue();
            return;
        }

        // For reference types: write null if the inner value is null.
        // For value types (structs): Value is never null, so skip this branch entirely and
        // always serialize the concrete value below.
        if (default(T) is null && value.Value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Value, options);
    }
}
