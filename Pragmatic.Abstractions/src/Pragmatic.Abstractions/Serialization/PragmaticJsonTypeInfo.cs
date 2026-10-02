using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Serialization;

/// <summary>
///     Fetches the metadata for a type from a resolver chain, for call sites that know their type at
///     compile time.
/// </summary>
/// <remarks>
///     Generated code serializes types it knows statically, so it has no business calling the
///     reflection-based overloads: they carry <c>RequiresUnreferencedCode</c>, and under Native AOT
///     with the fallback disabled they throw. One helper keeps the generated call sites to one line
///     and the failure — an uncovered type — to one clear message.
/// </remarks>
public static class PragmaticJsonTypeInfo
{
    /// <summary>The metadata for <typeparamref name="T" /> under these options.</summary>
    /// <exception cref="InvalidOperationException">
    ///     No resolver covers the type. Loud rather than silent: the alternative is a payload that
    ///     serializes to <c>{}</c> and a receiver that sees an object with no fields.
    /// </exception>
    public static JsonTypeInfo<T> For<T>(JsonSerializerOptions options)
        // TryGetTypeInfo, so the message below is the one the caller sees: GetTypeInfo throws its own
        // NotSupportedException first, which names the resolver rather than what to do about it.
        => options.TryGetTypeInfo(typeof(T), out var info) && info is JsonTypeInfo<T> typed
            ? typed
            : throw new InvalidOperationException(
                $"No JSON metadata for '{typeof(T)}'. Add it to a JsonSerializerContext registered with " +
                "PragmaticJsonOptions, or enable <PragmaticGenerateJsonContext> so the generator covers it.");
}
