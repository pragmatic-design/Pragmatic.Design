using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Pragmatic.Result.AspNetCore.OpenApi;

/// <summary>
/// Schema transformer that adjusts Result type schemas in OpenAPI documents.
/// </summary>
public sealed class ResultSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        // Check if this is a Result type
        if (IsResultType(type))
        {
            TransformResultSchema(schema, type);
        }
        else if (IsVoidResultType(type))
        {
            TransformVoidResultSchema(schema, type);
        }
        else if (IsMaybeType(type))
        {
            TransformMaybeSchema(schema, type);
        }

        return Task.CompletedTask;
    }

    private static bool IsResultType(Type type)
    {
        return type.IsGenericType &&
               type.GetGenericTypeDefinition() == typeof(Result<,>);
    }

    private static bool IsVoidResultType(Type type)
    {
        return type.IsGenericType &&
               type.GetGenericTypeDefinition() == typeof(VoidResult<>);
    }

    private static bool IsMaybeType(Type type)
    {
        return type.IsGenericType &&
               type.GetGenericTypeDefinition() == typeof(Maybe<>);
    }

    private static void TransformResultSchema(OpenApiSchema schema, Type resultType)
    {
        var typeArgs = resultType.GetGenericArguments();
        var valueType = typeArgs[0];
        var errorType = typeArgs[1];

        schema.Description =
            $"Result of an operation that returns {valueType.Name} or fails with {errorType.Name}. " +
            "The boolean 'isSuccess' selects between the value and error payloads.";

        // #11: intentionally NO Discriminator. OpenAPI discriminators must key on a *string* property
        // whose values map to schema names (RFC/OpenAPI spec). A discriminator over a boolean with no
        // Mapping is non-standard — NSwag/kiota either reject it or emit a broken client. The 'isSuccess'
        // flag is documented above and remains a plain schema property instead.
    }

    private static void TransformVoidResultSchema(OpenApiSchema schema, Type resultType)
    {
        var errorType = resultType.GetGenericArguments()[0];

        schema.Description =
            $"Result of a void operation that may fail with {errorType.Name}. " +
            "The boolean 'isSuccess' indicates whether the operation succeeded.";

        // #11: no Discriminator — see TransformResultSchema.
    }

    private static void TransformMaybeSchema(OpenApiSchema schema, Type maybeType)
    {
        var valueType = maybeType.GetGenericArguments()[0];

        schema.Description = $"Optional value that may or may not contain {valueType.Name}";

        // In OpenApi v2, nullable is expressed via type flags instead of a separate property
        schema.Type = (schema.Type ?? JsonSchemaType.Object) | JsonSchemaType.Null;
    }
}
