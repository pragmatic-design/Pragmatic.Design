using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Pragmatic.Result.AspNetCore.OpenApi;

/// <summary>
/// OpenAPI document transformer that enhances Result type schemas.
/// </summary>
/// <remarks>
/// <para>
/// This transformer automatically adjusts OpenAPI schemas for endpoints that return Result types,
/// adding proper response codes and ProblemDetails schemas for error cases.
/// </para>
/// <para>
/// Register in Program.cs:
/// <code>
/// builder.Services.AddOpenApi(options =>
/// {
///     options.AddDocumentTransformer&lt;ResultOpenApiTransformer&gt;();
/// });
/// </code>
/// </para>
/// </remarks>
public sealed class ResultOpenApiTransformer : IOpenApiDocumentTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        // Ensure ProblemDetails schema exists
        EnsureProblemDetailsSchema(document);

        // Process all operations
        if (document.Paths is not null)
        {
            foreach (var pathItem in document.Paths.Values)
            {
                if (pathItem.Operations is null)
                    continue;
                foreach (var operation in pathItem.Operations.Values)
                {
                    TransformOperation(operation, document);
                }
            }
        }

        return Task.CompletedTask;
    }

    private static void EnsureProblemDetailsSchema(OpenApiDocument document)
    {
        const string problemDetailsSchemaName = "ProblemDetails";

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();

        if (document.Components.Schemas.ContainsKey(problemDetailsSchemaName))
            return;

        document.Components.Schemas[problemDetailsSchemaName] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "RFC 7807 Problem Details for HTTP APIs",
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["type"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "A URI reference that identifies the problem type",
                    Example = JsonValue.Create("https://httpstatuses.io/404")
                },
                ["title"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "A short, human-readable summary of the problem type",
                    Example = JsonValue.Create("Not Found")
                },
                ["status"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Description = "The HTTP status code",
                    Example = JsonValue.Create(404)
                },
                ["detail"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Null,
                    Description = "A human-readable explanation specific to this occurrence"
                },
                ["instance"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Null,
                    Description = "A URI reference that identifies the specific occurrence"
                },
                ["code"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "Application-specific error code",
                    Example = JsonValue.Create("NOT_FOUND")
                }
            },
            AdditionalPropertiesAllowed = true
        };
    }

    private static void TransformOperation(OpenApiOperation operation, OpenApiDocument document)
    {
        if (operation.Responses is null)
            return;

        // Check if this operation already has typed error responses (from generated Produces<T>())
        var hasTypedErrorResponses = operation.Responses.Keys.Any(k => !k.StartsWith("2"));

        foreach (var response in operation.Responses.ToList())
        {
            if (!response.Key.StartsWith("2") || !IsResultTypeResponse(response.Value))
                continue;

            // This is a success response with Result type.
            // Only add generic error codes if the endpoint has NO typed error responses.
            // When the Endpoints SG generates Produces<SpecificError>(statusCode),
            // those already appear in the operation — adding generic ones would be noise.
            if (!hasTypedErrorResponses)
            {
                AddErrorResponseIfMissing(operation, "400", "Bad Request", document);
                AddErrorResponseIfMissing(operation, "401", "Unauthorized", document);
                AddErrorResponseIfMissing(operation, "403", "Forbidden", document);
                AddErrorResponseIfMissing(operation, "404", "Not Found", document);
                AddErrorResponseIfMissing(operation, "409", "Conflict", document);
                AddErrorResponseIfMissing(operation, "500", "Internal Server Error", document);
            }
        }
    }

    private static bool IsResultTypeResponse(IOpenApiResponse response)
    {
        // Check if response schema contains Result-like structure
        if (response.Content is null)
            return false;

        foreach (var content in response.Content.Values)
        {
            if (content.Schema?.Properties is not null)
            {
                // Check for Result<T,E> structure
                if (content.Schema.Properties.ContainsKey("isSuccess") &&
                    (content.Schema.Properties.ContainsKey("value") ||
                     content.Schema.Properties.ContainsKey("error")))
                {
                    return true;
                }
            }

            // Check for Result type by reference name
            if (content.Schema is OpenApiSchemaReference schemaRef &&
                (schemaRef.Reference.Id?.StartsWith("Result") == true ||
                 schemaRef.Reference.Id?.StartsWith("VoidResult") == true))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddErrorResponseIfMissing(
        OpenApiOperation operation,
        string statusCode,
        string description,
        OpenApiDocument document)
    {
        if (operation.Responses!.ContainsKey(statusCode))
            return;

        operation.Responses[statusCode] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchemaReference("ProblemDetails", document)
                }
            }
        };
    }
}
