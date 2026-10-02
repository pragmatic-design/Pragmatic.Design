using System.Collections.Concurrent;
using System.ComponentModel;

namespace Pragmatic.Result;

/// <summary>
///     Describes an error type's schema metadata for OpenAPI generation,
///     eliminating the need for reflection-based property discovery.
/// </summary>
/// <param name="Code">The application error code (e.g., "NOT_FOUND").</param>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="Title">The human-readable error title.</param>
/// <param name="ExtensionProperties">Custom extension property descriptors for OpenAPI.</param>
public sealed record ErrorSchemaMetadata(
    string Code,
    int StatusCode,
    string Title,
    IReadOnlyList<ErrorPropertyDescriptor> ExtensionProperties);

/// <summary>
///     Describes a custom extension property on an error type for OpenAPI schema generation.
/// </summary>
/// <param name="CamelCaseName">The property name in camelCase (as serialized in JSON).</param>
/// <param name="JsonSchemaType">The JSON Schema type string (e.g., "string", "integer", "boolean").</param>
/// <param name="Description">A human-readable description of the property.</param>
/// <param name="IsNullable">Whether the property is nullable.</param>
/// <param name="EnumValues">For enum properties, the allowed string values. Null for non-enum.</param>
public readonly record struct ErrorPropertyDescriptor(
    string CamelCaseName,
    string JsonSchemaType,
    string Description,
    bool IsNullable = false,
    IReadOnlyList<string>? EnumValues = null);

/// <summary>
///     Static registry for error type schema metadata.
///     The source generator registers metadata here at module initialization,
///     and the OpenAPI enricher reads it instead of using reflection.
/// </summary>
public static class ErrorSchemaRegistry
{
    private static readonly ConcurrentDictionary<Type, ErrorSchemaMetadata> Registry = new();

    /// <summary>
    ///     Registers schema metadata for an error type.
    ///     Called by SG-generated module initializers.
    /// </summary>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="metadata">The schema metadata.</param>
    public static void Register<TError>(ErrorSchemaMetadata metadata) where TError : IError
        => Registry[typeof(TError)] = metadata;

    /// <summary>
    ///     Tries to get schema metadata for an error type.
    /// </summary>
    /// <param name="errorType">The error type.</param>
    /// <param name="metadata">The metadata, if registered.</param>
    /// <returns>True if metadata was found.</returns>
    public static bool TryGet(Type errorType, out ErrorSchemaMetadata? metadata)
        => Registry.TryGetValue(errorType, out metadata);

    /// <summary>
    ///     Clears all registered error schema metadata.
    ///     Intended for test cleanup to avoid cross-test contamination.
    /// </summary>
    /// <remarks>
    ///     <b>Test-only.</b> Calling this from production code wipes every registration the application
    ///     made at startup, silently breaking OpenAPI error-schema generation. Hidden from IntelliSense
    ///     via <see cref="EditorBrowsableAttribute"/> to discourage accidental use outside tests.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Clear() => Registry.Clear();
}
