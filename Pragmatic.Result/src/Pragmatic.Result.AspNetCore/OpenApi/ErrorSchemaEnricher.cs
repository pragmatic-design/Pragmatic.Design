using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Pragmatic.Result.AspNetCore.OpenApi;

/// <summary>
/// Schema transformer that enriches Pragmatic error types with ProblemDetails-style schemas.
/// </summary>
/// <remarks>
/// <para>
/// When an error type inherits from <see cref="Error"/> (e.g., NotFoundError, ConflictError),
/// this transformer replaces its raw record schema with a ProblemDetails-compatible schema
/// that documents the error's custom extension properties (EntityType, EntityId, etc.).
/// </para>
/// <para>
/// Reads only <see cref="ErrorSchemaRegistry"/> metadata (zero-reflection, AOT-safe). An error type
/// without registered metadata is left as it is: there is no reflective fallback.
/// </para>
/// </remarks>
public sealed class ErrorSchemaEnricher : IOpenApiSchemaTransformer
{
    /// <summary>
    ///     Base property names on Error/IError that are always serialized as standard ProblemDetails fields.
    ///     These are excluded from the "extensions" portion of the schema.
    /// </summary>
    private static readonly HashSet<string> BasePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "StatusCode", "Title", "MessageKey", "Parameters",
        "IsTransient", "RetryAfter", "TitleKey", "DescriptionKey",
        // EqualityContract is a compiler-generated property on records
        "EqualityContract"
    };

    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        if (!IsErrorType(type))
            return Task.CompletedTask;

        // The generator registers every concrete error it can name, in this assembly and in every module
        // that references Pragmatic.Result — including Pragmatic.Result's own errors.
        //
        // An error with no entry has nothing to enrich. There is no reflective fallback: discovering
        // extension properties with GetProperties and reading Code/StatusCode/Title off an
        // Activator-built instance would run on every request that documents an error.
        if (ErrorSchemaRegistry.TryGet(type, out var metadata) && metadata is not null)
            EnrichFromMetadata(schema, type, metadata);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Enriches the schema using registered <see cref="ErrorSchemaMetadata"/> (compile-time metadata).
    /// </summary>
    private static void EnrichFromMetadata(OpenApiSchema schema, Type type, ErrorSchemaMetadata metadata)
    {
        if (metadata.ExtensionProperties.Count == 0)
            return;

        schema.Description = $"ProblemDetails response for {type.Name} (HTTP {metadata.StatusCode}). " +
                             $"Code: \"{metadata.Code}\". " +
                             $"Custom extensions: {string.Join(", ", metadata.ExtensionProperties.Select(p => p.CamelCaseName))}.";

        schema.Properties ??= new Dictionary<string, IOpenApiSchema>();

        EnsureBaseField(schema, "type", JsonSchemaType.String,
            "Problem type URI", $"https://httpstatuses.io/{metadata.StatusCode}");
        EnsureBaseField(schema, "title", JsonSchemaType.String, "Error title", metadata.Title);
        EnsureBaseField(schema, "status", JsonSchemaType.Integer, "HTTP status code", metadata.StatusCode);
        EnsureBaseField(schema, "detail", JsonSchemaType.String | JsonSchemaType.Null,
            "Human-readable explanation");
        EnsureBaseField(schema, "code", JsonSchemaType.String, "Application error code", metadata.Code);

        foreach (var prop in metadata.ExtensionProperties)
        {
            if (schema.Properties.ContainsKey(prop.CamelCaseName))
                continue;

            schema.Properties[prop.CamelCaseName] = BuildPropertySchemaFromDescriptor(prop);
        }
    }

    private static bool IsErrorType(Type type)
    {
        return type is { IsAbstract: false, IsInterface: false } && typeof(IError).IsAssignableFrom(type);
    }


    private static void EnsureBaseField(
        OpenApiSchema schema, string name, JsonSchemaType type,
        string description, object? example = null)
    {
        if (schema.Properties!.ContainsKey(name))
            return;

        var field = new OpenApiSchema
        {
            Type = type,
            Description = description
        };

        if (example is string s)
            field.Example = JsonValue.Create(s);
        else if (example is int i)
        {
            field.Format = "int32";
            field.Example = JsonValue.Create(i);
        }

        schema.Properties[name] = field;
    }

    private static OpenApiSchema BuildPropertySchemaFromDescriptor(ErrorPropertyDescriptor descriptor)
    {
        var schemaType = descriptor.JsonSchemaType switch
        {
            "string" => JsonSchemaType.String,
            "integer" => JsonSchemaType.Integer,
            "number" => JsonSchemaType.Number,
            "boolean" => JsonSchemaType.Boolean,
            "array" => JsonSchemaType.Array,
            "object" => JsonSchemaType.Object,
            _ => JsonSchemaType.String
        };

        if (descriptor.IsNullable)
            schemaType |= JsonSchemaType.Null;

        var result = new OpenApiSchema
        {
            Type = schemaType,
            Description = descriptor.Description
        };

        if (descriptor.EnumValues is { Count: > 0 })
            result.Enum = descriptor.EnumValues.Select(n => (JsonNode)JsonValue.Create(n)!).ToList();

        return result;
    }


}
