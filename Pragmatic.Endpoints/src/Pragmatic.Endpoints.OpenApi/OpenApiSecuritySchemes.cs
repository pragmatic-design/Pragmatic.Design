using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using PragmaticScheme = Pragmatic.Abstractions.Http.OpenApiSecurityScheme;

namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     How callers authenticate, as both published documents say it: what the registered
///     <see cref="Abstractions.Http.IOpenApiSecuritySchemeContributor" />s describe, and nothing else.
/// </summary>
/// <remarks>
///     <para>
///         One place for the rule, because there are two documents. The compile-time one
///         (<c>MapPragmaticOpenApi</c>) composes these into its JSON; the runtime one, enriched by
///         <see cref="ManifestOpenApiTransformer" />, adds them to ASP.NET's document object.
///     </para>
///     <para>
///         ⚠️ No <c>Bearer</c> scheme of its own, "the most common choice", required on every protected
///         operation: the compile-time document makes no such guess either, because a guessed scheme
///         fails in the integrator's application.
///     </para>
/// </remarks>
internal static partial class OpenApiSecuritySchemes
{
    /// <summary>
    ///     The schemes the registered contributors describe. When there are none and some operation
    ///     requires authentication, the gap is logged — not filled.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="requiresAuthentication">Whether the document has an operation that requires it.</param>
    public static IReadOnlyList<PragmaticScheme> Describe(IServiceProvider services, bool requiresAuthentication)
    {
        var schemes = services
            .GetServices<Abstractions.Http.IOpenApiSecuritySchemeContributor>()
            .Select(c => c.Describe())
            .ToList();

        if (schemes.Count == 0 && requiresAuthentication
            && services.GetService<ILoggerFactory>() is { } loggers)
            LogNoSchemeDescribed(loggers.CreateLogger(typeof(OpenApiSecuritySchemes).FullName!));

        return schemes;
    }

    /// <summary>A contributed scheme as ASP.NET's document object holds it.</summary>
    /// <exception cref="InvalidOperationException">
    ///     The scheme names a type or a location OpenAPI does not have. A contributor wrote it, and a
    ///     document that cannot say it must not say something else instead.
    /// </exception>
    public static OpenApiSecurityScheme ToDocumentScheme(PragmaticScheme scheme) => new()
    {
        Type = scheme.Type switch
        {
            "http" => SecuritySchemeType.Http,
            "apiKey" => SecuritySchemeType.ApiKey,
            "oauth2" => SecuritySchemeType.OAuth2,
            "openIdConnect" => SecuritySchemeType.OpenIdConnect,
            "mutualTLS" => SecuritySchemeType.MutualTLS,
            _ => throw new InvalidOperationException(
                $"Security scheme '{scheme.Name}' declares type '{scheme.Type}', which OpenAPI does not have: "
                + "use http, apiKey, oauth2, openIdConnect or mutualTLS."),
        },
        Scheme = scheme.Scheme,
        BearerFormat = scheme.BearerFormat,
        Name = scheme.ParameterName,
        In = scheme.In switch
        {
            null => null,
            "header" => ParameterLocation.Header,
            "query" => ParameterLocation.Query,
            "cookie" => ParameterLocation.Cookie,
            _ => throw new InvalidOperationException(
                $"Security scheme '{scheme.Name}' travels in '{scheme.In}', which OpenAPI does not have: "
                + "use header, query or cookie."),
        },
        OpenIdConnectUrl = scheme.OpenIdConnectUrl is null ? null : new Uri(scheme.OpenIdConnectUrl),
        Description = scheme.Description,
    };

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The published contract has operations that require authentication and no "
                  + "IOpenApiSecuritySchemeContributor is registered, so it does not say how to "
                  + "authenticate. Register one where the authentication is configured.")]
    private static partial void LogNoSchemeDescribed(ILogger logger);
}
