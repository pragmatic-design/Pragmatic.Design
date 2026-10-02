using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition;

namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     Publishes the API contract outside Development: <c>app.UseApiDocumentation()</c>.
/// </summary>
/// <remarks>
///     A host that references this package publishes the compile-time document at
///     <c>/openapi/v1.json</c> in Development without being asked. Every other environment is a decision
///     the application states here, because a published contract is a surface: whoever can reach the
///     host can read every operation it exposes, and the permission each one requires.
/// </remarks>
public static class PragmaticBuilderApiDocumentationExtensions
{
    /// <summary>Publishes the document in every environment, not only in Development.</summary>
    /// <param name="builder">The Pragmatic builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <example>
    ///     <code>
    ///     await PragmaticApp.RunAsync(args, app =>
    ///     {
    ///         app.UseApiDocumentation();
    ///     });
    ///     </code>
    /// </example>
    public static IPragmaticBuilder UseApiDocumentation(this IPragmaticBuilder builder)
    {
        Ensure.Ensure.ThrowIfNull(builder);

        builder.Services.Configure<ApiDocumentationOptions>(options => options.PublishInEveryEnvironment = true);
        return builder;
    }
}
