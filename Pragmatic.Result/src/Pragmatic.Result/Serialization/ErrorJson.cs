using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Result.Serialization;

/// <summary>
///     Reads and writes an error on the wire when the concrete type is known at compile time.
/// </summary>
/// <remarks>
///     The public, typed counterpart of the internal <c>ErrorJsonHelper</c>, which serializes by
///     <c>error.GetType()</c> — polymorphism resolved at run time, and so neither trim- nor AOT-safe.
///     A generated converter does not need that: it knows the result's declared error slots, so it can
///     switch on the concrete type and hand the matching <see cref="JsonTypeInfo{T}" /> straight in.
///     <para>
///         The wire shape is unchanged — <c>{"$errorType": "…", …the error's own properties…}</c> — so a
///         payload written by either path reads back through the other.
///     </para>
/// </remarks>
public static class ErrorJson
{
    /// <summary>
    ///     Writes the error as a flat object carrying its discriminator.
    /// </summary>
    /// <typeparam name="TError">The concrete error type.</typeparam>
    /// <param name="writer">The writer.</param>
    /// <param name="error">The error.</param>
    /// <param name="typeInfo">Metadata for <typeparamref name="TError" />.</param>
    /// <param name="discriminator">
    ///     The value to write under <c>$errorType</c>. Must match what
    ///     <see cref="ErrorTypeRegistry.GetDiscriminator" /> produces for the type, or the reader will
    ///     not resolve it back.
    /// </param>
    public static void Write<TError>(
        Utf8JsonWriter writer,
        TError error,
        JsonTypeInfo<TError> typeInfo,
        string discriminator)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(typeInfo);

        // Serialize first, then re-emit the properties alongside the discriminator, so the error stays
        // one flat object instead of gaining a nesting level.
        var element = JsonSerializer.SerializeToElement(error, typeInfo);

        writer.WriteStartObject();
        writer.WriteString(ErrorTypeRegistry.DiscriminatorProperty, discriminator);

        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                // Defensive: skip a discriminator the type may carry itself.
                if (property.NameEquals(ErrorTypeRegistry.DiscriminatorProperty))
                    continue;
                property.WriteTo(writer);
            }

        writer.WriteEndObject();
    }

    /// <summary>
    ///     Reads the <c>$errorType</c> discriminator, or null when the element carries none.
    /// </summary>
    /// <param name="errorElement">The buffered error element.</param>
    /// <returns>The discriminator, or null.</returns>
    public static string? ReadDiscriminator(JsonElement errorElement)
    {
        if (errorElement.ValueKind != JsonValueKind.Object
            || !errorElement.TryGetProperty(ErrorTypeRegistry.DiscriminatorProperty, out var element)
            || element.ValueKind != JsonValueKind.String)
            return null;

        return element.GetString();
    }

    /// <summary>
    ///     Reads an error of a known concrete type.
    /// </summary>
    /// <typeparam name="TError">The concrete error type.</typeparam>
    /// <param name="errorElement">The buffered error element.</param>
    /// <param name="typeInfo">Metadata for <typeparamref name="TError" />.</param>
    /// <returns>The error.</returns>
    /// <exception cref="JsonException">Thrown when the element does not produce an error.</exception>
    public static TError Read<TError>(JsonElement errorElement, JsonTypeInfo<TError> typeInfo)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        return errorElement.Deserialize(typeInfo)
               ?? throw new JsonException($"The 'error' payload did not produce a {typeof(TError)}.");
    }
}
