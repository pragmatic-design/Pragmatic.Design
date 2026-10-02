using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Configuration rendering for EndpointHandlerTemplate.
/// </summary>
internal sealed partial class EndpointHandlerTemplate
{
    // Parameterize the builder variable so versioned endpoints can apply the SAME
    // shared configuration (auth, permissions, rate-limit, caching, metadata) to EVERY mapped
    // version, not just the last one. Defaults to "builder" for the non-versioned path.
    private void RenderEndpointConfiguration(string b = "builder")
    {
        // Name
        if (!string.IsNullOrEmpty(_model.Name))
            AppendLine($"{b}.WithName(\"{EscapeString(_model.Name)}\");");

        // OpenAPI
        if (!string.IsNullOrEmpty(_model.Summary))
            AppendLine($"{b}.WithSummary(\"{EscapeString(_model.Summary!)}\");");

        if (!string.IsNullOrEmpty(_model.Description))
            AppendLine($"{b}.WithDescription(\"{EscapeString(_model.Description!)}\");");

        if (!_model.Tags.IsDefaultOrEmpty)
        {
            var tags = string.Join(", ", _model.Tags.Select(t => $"\"{t}\""));
            AppendLine($"{b}.WithTags({tags});");
        }
        else
        {
            // Default tag derived from route (or entity name fallback for relative routes)
            var entityName = (_model.MutationEntityType ?? _model.QueryEntityType)?.Split('.').LastOrDefault();
            var defaultTag = EndpointTagHelper.DeriveTagFromRoute(_model.Route, entityName);
            if (defaultTag is not null)
                AppendLine($"{b}.WithTags(\"{defaultTag}\");");
        }

        // A route that belongs to no tenant says so, and the tenant middleware reads it. Separate
        // from the authorization block below because the two are separate questions:
        // [AllowAnonymous] lifts authentication, and the tenant refusal happens at order 92, before
        // the route runs and whoever is asking — which is why an anonymous probe was answered 400 in
        // a multi-tenant host.
        if (_model.Authorization?.RouteIsTenantAgnostic == true)
            AppendLine(EndpointMetadataRenderer.RenderTenantAgnostic(b));

        // Authorization
        if (_model.Authorization is not null)
        {
            if (_model.Authorization.AllowAnonymous)
            {
                AppendLine($"{b}.AllowAnonymous();");
            }
            else if (_model.Authorization.IsRequired)
            {
                if (!string.IsNullOrEmpty(_model.Authorization.PolicyName))
                {
                    AppendLine($"{b}.RequireAuthorization(\"{StringHelper.CSharpLiteral(_model.Authorization.PolicyName)}\");");
                }
                else if (!_model.Authorization.RequiredPermissions.IsDefaultOrEmpty)
                {
                    RenderRequiredPermissions(_model.Authorization.RequiredPermissions.AsImmutableArray(), b);
                }
                else if (!_model.Authorization.AnyPermissions.IsDefaultOrEmpty)
                {
                    RenderAnyPermissions(_model.Authorization.AnyPermissions.AsImmutableArray(), b);
                }
                else if (!_model.Authorization.RequiredRoles.IsDefaultOrEmpty)
                {
                    var roles = string.Join(", ", _model.Authorization.RequiredRoles.Select(r => $"\"{r}\""));
                    AppendLine($"{b}.RequireAuthorization(policy => policy.RequireRole({roles}));");
                }
                else
                {
                    AppendLine($"{b}.RequireAuthorization();");
                }
            }
        }
        // No explicit authorization → handled centrally at the root group via
        // PragmaticEndpointsOptions.RequireAuthorizationByDefault (default true; see EndpointRuntimeConfigRenderer).
        // A per-endpoint RequireAuthorization() here would duplicate that option and crash hosts without
        // UseAuthorization(). Opt out per-endpoint with [AllowAnonymous]; gate with [RequirePermission].

        // API Versioning
        if (!_model.ApiVersions.IsDefaultOrEmpty)
            foreach (var version in _model.ApiVersions)
                if (version.Deprecated)
                    AppendLine(
                        $"{b}.WithMetadata(new Microsoft.AspNetCore.Mvc.ApiVersionAttribute(\"{version.Version}\") {{ Deprecated = true }});");

        // Rate limiting
        var rateLimitPolicy = RateLimitHelper.GetPolicyName(_model);
        if (rateLimitPolicy is not null)
            AppendLine($"{b}.RequireRateLimiting(\"{StringHelper.CSharpLiteral(rateLimitPolicy)}\");");

        // Request body size limit ([MaxBodySize]) — enforced by RequestLimitsStep before the body is read
        if (_model.MaxBodySizeBytes is > 0)
            AppendLine($"{b}.WithMetadata(new global::Pragmatic.Http.MaxBodySizeMetadata({_model.MaxBodySizeBytes}L));");

        // Response caching
        if (_model.ResponseCache is not null)
            RenderCacheOutput(_model.ResponseCache, b);

        // Antiforgery ([RequireAntiforgery]) — enforced by the antiforgery middleware
        if (_model.RequireAntiforgery)
            AppendLine($"{b}.WithMetadata(new Microsoft.AspNetCore.Antiforgery.RequireAntiforgeryTokenAttribute());");

        // Idempotency ([Idempotent]) — replay cache keyed on header + body hash
        foreach (var line in IdempotencyFilterHelper.RenderLines(_model, b))
            AppendLine(line);

        // Form data configuration — [RequireAntiforgery] keeps token validation on
        if (_model.HasFormParams)
        {
            if (!_model.RequireAntiforgery)
                AppendLine($"{b}.DisableAntiforgery();");
            if (_model.FormParameters.Any(p => p.IsFile))
                AppendLine(EndpointMetadataRenderer.RenderAccepts(b, "Microsoft.AspNetCore.Http.IFormFile", "multipart/form-data"));
        }

        // The request body, for OpenAPI. A generated endpoint is mapped as a RequestDelegate so it
        // survives an AOT publish, and a RequestDelegate has no typed parameters — so nothing can infer
        // what it accepts. Only the multipart case ever said so, which left the document describing the
        // responses of a POST and not its body: 87 of 89 endpoints in the reference application.
        if (_model.NeedsBodyDto && !_model.HasFormParams)
        {
            foreach (var variant in _model.BodyDtoVariants)
            {
                AppendLine(EndpointMetadataRenderer.RenderAccepts(
                    b, $"global::{_model.Namespace}.{variant.Name}", "application/json"));
            }
        }

        // Produces metadata for OpenAPI
        RenderProducesMetadata(b);
    }

    /// <summary>
    ///     Renders RequiredPermissions (All mode) authorization.
    ///     When Identity.AspNetCore is available, uses PragmaticPermissionRequirement.
    ///     Otherwise falls back to inline RequireClaim checks.
    /// </summary>
    private void RenderRequiredPermissions(System.Collections.Immutable.ImmutableArray<string> permissions, string b = "builder")
    {
        var permArray = string.Join(", ", permissions.Select(p => $"\"{p}\""));

        AppendLine($"{b}.RequireAuthorization(policy => policy.AddRequirements(");
        IncreaseIndent();
        AppendLine("new global::Pragmatic.Endpoints.Authorization.PragmaticPermissionRequirement(");
        IncreaseIndent();
        AppendLine($"new[] {{ {permArray} }},");
        AppendLine("global::Pragmatic.Endpoints.Authorization.PermissionMode.All)));");
        DecreaseIndent();
        DecreaseIndent();
    }

    /// <summary>
    ///     Renders AnyPermissions (Any mode) authorization.
    ///     When Identity.AspNetCore is available, uses PragmaticPermissionRequirement.
    ///     Otherwise falls back to inline RequireAssertion checks.
    /// </summary>
    private void RenderAnyPermissions(System.Collections.Immutable.ImmutableArray<string> permissions, string b = "builder")
    {
        var permArray = string.Join(", ", permissions.Select(p => $"\"{p}\""));

        AppendLine($"{b}.RequireAuthorization(policy => policy.AddRequirements(");
        IncreaseIndent();
        AppendLine("new global::Pragmatic.Endpoints.Authorization.PragmaticPermissionRequirement(");
        IncreaseIndent();
        AppendLine($"new[] {{ {permArray} }},");
        AppendLine("global::Pragmatic.Endpoints.Authorization.PermissionMode.Any)));");
        DecreaseIndent();
        DecreaseIndent();
    }

    // Honor Location/NoStore with real HTTP cache-control semantics.
    //   NoStore / Location.None → emit "Cache-Control: no-store" (and no server cache).
    //   Location.Client         → emit "Cache-Control: private[, max-age=N]" (private = not server/proxy cached).
    //   Location.Any (default)  → server-side OutputCache as before (backward-compatible).
    // The header is set via an endpoint filter because ASP.NET Core OutputCache has no client-directive API.
    private void RenderCacheOutput(Models.ResponseCacheModel cache, string b = "builder")
    {
        var location = cache.Location ?? "Any";

        if (cache.NoStore || location == "None")
        {
            RenderCacheControlHeader(b, "no-store");
            return;
        }

        if (location == "Client")
        {
            var value = cache.Duration > 0 ? $"private, max-age={cache.Duration}" : "private";
            RenderCacheControlHeader(b, value);
            return;
        }

        // Location.Any → shared server-side output caching.
        var hasVaryBy = !cache.VaryByQueryKeys.IsDefaultOrEmpty || !cache.VaryByHeaders.IsDefaultOrEmpty;
        var hasProfile = !string.IsNullOrEmpty(cache.Profile);

        if (!hasVaryBy && !hasProfile)
        {
            // Simple case: just expire
            AppendLine($"{b}.CacheOutput(c => c.Expire(System.TimeSpan.FromSeconds({cache.Duration})));");
            return;
        }

        // Multi-line CacheOutput lambda
        AppendLine($"{b}.CacheOutput(c =>");
        AppendLine("{");
        IncreaseIndent();

        AppendLine($"c.Expire(System.TimeSpan.FromSeconds({cache.Duration}));");

        if (!cache.VaryByQueryKeys.IsDefaultOrEmpty)
        {
            var keys = string.Join(", ", cache.VaryByQueryKeys.Select(k => $"\"{k}\""));
            AppendLine($"c.SetVaryByQuery({keys});");
        }

        if (!cache.VaryByHeaders.IsDefaultOrEmpty)
        {
            var headers = string.Join(", ", cache.VaryByHeaders.Select(h => $"\"{h}\""));
            AppendLine($"c.SetVaryByHeader({headers});");
        }

        if (hasProfile)
            AppendLine($"c.Tag(\"{cache.Profile}\");");

        DecreaseIndent();
        AppendLine("});");
    }

    private void RenderCacheControlHeader(string b, string headerValue)
    {
        AppendLine($"{b}.AddEndpointFilter(async (cacheCtx, cacheNext) =>");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"cacheCtx.HttpContext.Response.Headers.CacheControl = \"{headerValue}\";");
        AppendLine("return await cacheNext(cacheCtx);");
        DecreaseIndent();
        AppendLine("});");
    }

    private void RenderProducesMetadata(string b = "builder")
    {
        // Success response
        if (_model.IsStreamingResponse)
        {
            AppendLine(EndpointMetadataRenderer.RenderProduces(b, _model.StreamItemTypeName!, 200, "text/event-stream"));
        }
        else if (_model.IsVoid)
        {
            AppendLine(EndpointMetadataRenderer.RenderProducesEmpty(b, 204));
        }
        else if (_model.IsFileResponse)
        {
            // The body is bytes, not the FileResponse record: Produces<FileResponse> would publish a
            // schema with a Stream property that no client can consume.
            AppendLine($"{b}.WithMetadata(new global::Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata(200, null, new[] {{ \"application/octet-stream\" }}));");
        }
        else if (!string.IsNullOrEmpty(_model.ResponseType))
        {
            var successCode = _model.ComputedSuccessStatusCode;
            AppendLine(EndpointMetadataRenderer.RenderProduces(b, _model.ResponseType!, successCode));
        }

        // Error responses — typed Produces<TError>(statusCode).
        //
        // Deliberately the error TYPE and not ProblemDetails, even though ProblemDetails is what goes on
        // the wire: naming the error type is what gives OpenAPI a schema per error, which
        // ErrorSchemaEnricher then reshapes into RFC 7807 by adding the base fields and the error's own
        // properties as extensions. Pointing at ProblemDetails directly would collapse every error to
        // the same schema and lose those extensions.
        //
        // A streaming endpoint is excluded: its failures travel IN BAND, as an SseStreamEvent.FromError
        // on a response that already answered 200. Declaring a 404 there described a status the
        // generated handler has no path to return.
        if (!_model.IsStreamingResponse)
            foreach (var error in _model.ErrorTypes)
                AppendLine(EndpointMetadataRenderer.RenderProduces(b, error.TypeName, error.StatusCode));
    }
}
