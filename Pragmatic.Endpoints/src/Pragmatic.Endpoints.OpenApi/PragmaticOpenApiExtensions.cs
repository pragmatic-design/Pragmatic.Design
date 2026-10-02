using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     Extension methods for Pragmatic OpenAPI — compile-time generated spec.
/// </summary>
public static class PragmaticOpenApiExtensions
{
    /// <summary>
    ///     Adds Pragmatic OpenAPI enrichment to the Microsoft OpenAPI pipeline.
    ///     Enriches the runtime-generated spec with manifest data (errors, permissions).
    /// </summary>
    public static IServiceCollection AddPragmaticOpenApi(this IServiceCollection services)
    {
        services.ConfigureAll<OpenApiOptions>(options =>
        {
            options.AddDocumentTransformer<ManifestOpenApiTransformer>();
        });
        return services;
    }

    /// <summary>
    ///     Maps the compile-time generated OpenAPI JSON at the specified path.
    ///     This serves the SG-generated OpenAPI spec directly — no runtime generation needed.
    ///     Use instead of MapOpenApi() for a fully static spec.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The URL pattern. Default: /openapi/v1.json</param>
    public static IEndpointConventionBuilder MapPragmaticOpenApi(
        this IEndpointRouteBuilder endpoints, string pattern = "/openapi/v1.json")
    {
        // Composed once, on the first request: the schemes come from the container and cannot change
        // while the process lives, so recomposing per request would rebuild the same string.
        string? composed = null;

        return endpoints.MapGet(pattern, (HttpContext context) =>
        {
            var document = FindOpenApiDocument(context.RequestServices);
            if (document is null)
                return Results.NotFound("No compile-time OpenAPI spec found. Ensure the SG generated PragmaticOpenApi.");

            composed ??= Compose(document, context.RequestServices);

            return Results.Text(composed, "application/json");
        })
        .ExcludeFromDescription()
        // The contract belongs to no tenant: it is what a caller fetches before being anyone, and a
        // multi-tenant host would otherwise refuse it with 400 for want of a header the caller has no
        // way to know it needs. Declared here so the route stays reachable wherever it is mapped.
        .WithMetadata(global::Pragmatic.MultiTenancy.TenantAgnosticEndpoint.Instance);
    }

    /// <summary>
    ///     The document <b>this host</b> publishes: from its own container, falling back to the
    ///     process-wide registry.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The container first, and the order matters: the registry is one field for the whole
    ///     process, written per host by a <c>[ModuleInitializer]</c>, so with two hosts the one loaded
    ///     second would answer for both. The fallback stays for a host that registers nothing.
    /// </remarks>
    private static HostOpenApiDocument? FindOpenApiDocument(IServiceProvider services)
    {
        var registered = services.GetService<HostOpenApiDocument>();
        if (registered is not null)
            return registered;

        return PragmaticOpenApiRegistry.Json is { } json
            ? new HostOpenApiDocument(json, PragmaticOpenApiRegistry.RequiresAuthentication)
            : null;
    }

    /// <summary>
    ///     The document as this host publishes it: with its security schemes, and without the Pragmatic
    ///     operations when <c>EnableOpenApi</c> is off.
    /// </summary>
    /// <remarks>
    ///     The options are resolved the way the generated endpoint root resolves them — the registered
    ///     instance, or the defaults — so the two cannot disagree about what the host configured.
    /// </remarks>
    private static string Compose(HostOpenApiDocument document, IServiceProvider services)
    {
        var secured = ComposeSecurity(document, services);
        var options = services.GetService<global::Pragmatic.Endpoints.Configuration.PragmaticEndpointsOptions>()
                      ?? new global::Pragmatic.Endpoints.Configuration.PragmaticEndpointsOptions();

        return options.EnableOpenApi ? secured : PragmaticOpenApiDocument.WithoutOperations(secured);
    }

    /// <summary>
    ///     Adds the security schemes whoever set the authentication up registered.
    /// </summary>
    /// <remarks>
    ///     ⚠️ When the document has operations that require authentication and nothing described how,
    ///     the gap is <b>logged</b>, not filled. Filling it would mean guessing — bearer, because three
    ///     of the framework entry points happen to use it — and a guess published as a
    ///     contract is worse than an absence: a client generated from it sends the wrong credential
    ///     and the failure surfaces at the far end, in someone else's application.
    /// </remarks>
    private static string ComposeSecurity(HostOpenApiDocument document, IServiceProvider services)
        => OpenApiSecurityComposer.Compose(
            document.Json,
            // This host's flag, from the same object as this host's document: read from the static
            // registry it would be the other host's answer whenever two of them share a process.
            OpenApiSecuritySchemes.Describe(services, document.RequiresAuthentication));
}
