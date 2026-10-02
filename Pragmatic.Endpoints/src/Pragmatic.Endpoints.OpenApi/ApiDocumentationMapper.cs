using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     Publishes the API contract from the generated host: the compile-time document in Development, or
///     everywhere after <c>UseApiDocumentation()</c>, and an interactive reference over it in Development.
/// </summary>
/// <remarks>
///     <para>
///         Without it every application would write the same startup step — map the document, map
///         Scalar in Development — and a host that forgot it would publish no contract at all. The
///         generated entry point calls this after every endpoint is mapped, so it is also where a hand-written mapping
///         is seen: an application that mapped the same route keeps it, and this one steps aside.
///         Two endpoints on one route is otherwise an <c>AmbiguousMatchException</c> on the first request.
///     </para>
/// </remarks>
public static partial class ApiDocumentationMapper
{
    /// <summary>The route the document is published on — the one <c>MapPragmaticOpenApi</c> defaults to.</summary>
    public const string DocumentPath = "/openapi/v1.json";

    /// <summary>Maps the document, and the reference when the host has one, where the environment allows.</summary>
    /// <param name="app">The application, used as route table, environment and container.</param>
    /// <param name="reference">The interactive reference the host references, or <c>null</c>.</param>
    public static void Map(WebApplication app, InteractiveApiReference? reference = null)
    {
        Ensure.Ensure.ThrowIfNull(app);

        var inDevelopment = app.Environment.IsDevelopment();
        var options = app.Services.GetRequiredService<IOptions<ApiDocumentationOptions>>().Value;
        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Pragmatic.Endpoints.OpenApi.ApiDocumentationMapper");

        if (inDevelopment || options.PublishInEveryEnvironment)
        {
            if (IsMapped(app, DocumentPath, prefix: false))
                LogAlreadyMapped(logger, "document", DocumentPath);
            else
                app.MapPragmaticOpenApi(DocumentPath);
        }

        if (reference is null || !inDevelopment)
            return;

        if (IsMapped(app, reference.PathPrefix, prefix: true))
        {
            LogAlreadyMapped(logger, "reference", reference.PathPrefix);
            return;
        }

        // The host's security headers default to a policy for an API — no script, no style, nothing
        // loaded — and the reference is an HTML page that is all of those. It states its own policy, which
        // the security headers never overwrite: without it the page loads blank, with the browser blaming
        // the policy in the console.
        var group = app.MapGroup(string.Empty);
        reference.Map(group);
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.ContentSecurityPolicy = ReferenceContentSecurityPolicy;
            return await next(context).ConfigureAwait(false);
        });
    }

    /// <summary>The policy the interactive reference answers with, in place of the host's API default.</summary>
    /// <remarks>
    ///     Scalar's page starts from an inline module script and styles itself inline, so both need
    ///     <c>'unsafe-inline'</c>; its fonts come from <c>fonts.scalar.com</c>. <c>ws:</c> is the development
    ///     tooling's refresh socket — the reference is mapped in Development only. Framing and foreign form
    ///     targets stay refused, as in the default.
    /// </remarks>
    public const string ReferenceContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline' https://fonts.scalar.com; " +
        "font-src 'self' data: https://fonts.scalar.com; img-src 'self' data: https:; connect-src 'self' ws: wss:; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "API {What} not mapped by the host: '{Path}' is already mapped by this application.")]
    private static partial void LogAlreadyMapped(ILogger logger, string what, string path);

    /// <summary>Whether an already-mapped endpoint answers on this path, or anywhere under it.</summary>
    /// <remarks>
    ///     Compared on the raw pattern, as <c>HealthEndpointMapper</c> does: this runs once at startup, the
    ///     paths are literals, and a matcher built to ask one question would cost more than the question.
    /// </remarks>
    private static bool IsMapped(IEndpointRouteBuilder app, string path, bool prefix)
    {
        var normalized = path.Trim('/');

        foreach (var dataSource in app.DataSources)
        {
            foreach (var endpoint in dataSource.Endpoints)
            {
                if (endpoint is not RouteEndpoint route)
                    continue;

                var pattern = route.RoutePattern.RawText?.Trim('/') ?? string.Empty;
                if (string.Equals(pattern, normalized, StringComparison.OrdinalIgnoreCase)
                    || (prefix && pattern.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }

        return false;
    }
}
