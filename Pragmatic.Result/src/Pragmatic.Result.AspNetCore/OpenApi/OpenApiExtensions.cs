using Microsoft.AspNetCore.OpenApi;

namespace Pragmatic.Result.AspNetCore.OpenApi;

/// <summary>
/// Extension methods for configuring OpenAPI with Result type support.
/// </summary>
public static class OpenApiExtensions
{
    /// <summary>
    /// Adds Result type support to OpenAPI document generation.
    /// </summary>
    /// <param name="options">The OpenAPI options</param>
    /// <returns>The options for chaining</returns>
    /// <remarks>
    /// <para>
    /// This method configures OpenAPI to properly document Result type responses,
    /// adding ProblemDetails schemas for error cases.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddOpenApi(options =>
    /// {
    ///     options.AddResultTypeSupport();
    /// });
    /// </code>
    /// </example>
    public static OpenApiOptions AddResultTypeSupport(this OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddDocumentTransformer<ResultOpenApiTransformer>();
        options.AddSchemaTransformer<ResultSchemaTransformer>();
        options.AddSchemaTransformer<ErrorSchemaEnricher>();
        options.AddSchemaTransformer<CommonSchemaEnricher>();

        return options;
    }
}
