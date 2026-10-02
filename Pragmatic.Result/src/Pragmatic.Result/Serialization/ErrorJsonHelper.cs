using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Pragmatic.Result.Serialization;

/// <summary>
///     Shared write/read logic for the polymorphic error payload used by
///     <see cref="ResultJsonConverter{TValue,TError}"/> and <see cref="VoidResultJsonConverter{TError}"/>.
/// </summary>
/// <remarks>
///     <para>
///         <b>Write.</b> The error is serialized by its RUNTIME type (not the declared <c>TError</c>, which
///         may be <see cref="IError"/>), and a <see cref="ErrorTypeRegistry.DiscriminatorProperty"/>
///         property is injected alongside the existing error properties. The output keeps its previous
///         shape (code, statusCode, title, extensions) PLUS the discriminator, so existing consumers are
///         unaffected.
///     </para>
///     <para>
///         <b>Read.</b> The discriminator is read first and resolved via <see cref="ErrorTypeRegistry"/>.
///         The element is then deserialized into the resolved concrete type. If the discriminator is
///         absent or unregistered, the element is deserialized into <see cref="SerializedError"/> as a
///         graceful fallback.
///     </para>
/// </remarks>
internal static class ErrorJsonHelper
{
    /// <summary>
    ///     Writes an error object, injecting the type discriminator alongside the error's own properties.
    /// </summary>
    /// <remarks>
    ///     Serializing by the error's runtime type is what enables polymorphic output, and it is the one
    ///     thing here that a trimmed or AOT build cannot do. Reached only from
    ///     <see cref="WriteDeclared{TError}"/>, which takes this branch when the runtime type differs
    ///     from the declared one and refuses it outright under Native AOT — so the suppression covers a
    ///     path that only a JIT runtime ever executes.
    /// </remarks>
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Polymorphic by runtime type; reached only on a JIT runtime, since WriteDeclared refuses this branch under Native AOT.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Polymorphic by runtime type; reached only on a JIT runtime, since WriteDeclared refuses this branch under Native AOT.")]
    public static void WriteError(Utf8JsonWriter writer, IError error, JsonSerializerOptions options)
    {
        var runtimeType = error.GetType();

        // Serialize by runtime type first, then re-emit its properties plus the discriminator into a
        // single flat object. This keeps the existing error shape (no nesting) while adding $errorType.
        var element = JsonSerializer.SerializeToElement(error, runtimeType, options);

        writer.WriteStartObject();
        writer.WriteString(ErrorTypeRegistry.DiscriminatorProperty, ErrorTypeRegistry.GetDiscriminator(runtimeType));

        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                // Defensive: skip a discriminator the runtime type may itself carry (e.g. SerializedError).
                if (property.NameEquals(ErrorTypeRegistry.DiscriminatorProperty))
                    continue;
                property.WriteTo(writer);
            }

        writer.WriteEndObject();
    }

    /// <summary>
    ///     Writes an error the caller has declared the type of, typed whenever the runtime type agrees.
    /// </summary>
    /// <remarks>
    ///     The fixed-arity converters call this rather than <see cref="WriteError" />: calling that
    ///     unconditionally would send every error through <c>error.GetType()</c> and a serializer
    ///     resolved for that type — not AOT-safe, and invisible to the gate because the warning it
    ///     would raise is suppressed on this file.
    ///     <para>
    ///         When the error is exactly <typeparamref name="TError" /> the metadata is known and the
    ///         write is typed. When it is not — <typeparamref name="TError" /> is <c>IError</c> or an
    ///         abstract base, or <c>Failure(IError)</c> was handed a subtype — the shape is genuinely
    ///         polymorphic. That still works on a JIT runtime and fails loudly under Native AOT, where
    ///         serializing an unnameable type produces an object with no fields rather than an error.
    ///     </para>
    /// </remarks>
    public static void WriteDeclared<TError>(Utf8JsonWriter writer, TError error, JsonSerializerOptions options)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error.GetType() == typeof(TError))
        {
            ErrorJson.Write(writer, error,
                global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<TError>(options),
                ErrorTypeRegistry.GetDiscriminator(typeof(TError)));
            return;
        }

        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new JsonException(
                $"Cannot write error '{error.GetType()}' declared as '{typeof(TError)}': the types differ, "
                + "and under Native AOT there is no runtime-type serializer to fall back on. Declare the "
                + "concrete error type on the result, or route it through [JsonResultContract<…>].");

        WriteError(writer, error, options);
    }

    /// <summary>
    ///     Reads an error from a buffered JSON element, resolving the concrete type via its discriminator
    ///     and falling back to <see cref="SerializedError"/> when the discriminator is missing or unknown.
    /// </summary>
    /// <remarks>
    ///     The <see cref="Type"/>-based <c>Deserialize</c> overload resolves the concrete type from the
    ///     discriminator, which is the polymorphism a JIT runtime can do and an AOT one cannot. A
    ///     generated converter never comes here: it switches over the slots its result declares.
    /// </remarks>
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Polymorphic by runtime type; reached only on a JIT runtime, since WriteDeclared refuses this branch under Native AOT.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Polymorphic by runtime type; reached only on a JIT runtime, since WriteDeclared refuses this branch under Native AOT.")]
    public static IError ReadError(JsonElement errorElement, JsonSerializerOptions options)
    {
        string? discriminator = null;
        if (errorElement.ValueKind == JsonValueKind.Object
            && errorElement.TryGetProperty(ErrorTypeRegistry.DiscriminatorProperty, out var discriminatorElement)
            && discriminatorElement.ValueKind == JsonValueKind.String)
            discriminator = discriminatorElement.GetString();

        if (ErrorTypeRegistry.TryResolve(discriminator, out var concreteType))
        {
            var resolved = (IError?)errorElement.Deserialize(concreteType, options);
            if (resolved is not null)
                return resolved;
        }

        // Graceful fallback: preserve code/status/title/extensions in a generic carrier.
        return errorElement.Deserialize<SerializedError>(options)
               ?? new SerializedError { OriginalErrorType = discriminator };
    }
}
