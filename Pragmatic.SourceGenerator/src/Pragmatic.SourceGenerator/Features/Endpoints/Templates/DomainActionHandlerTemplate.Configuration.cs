using System.Collections.Generic;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

// Endpoint configuration: metadata, authorization, OpenAPI, rate limiting, caching
internal sealed partial class DomainActionHandlerTemplate
{
    // Parameterize the builder variable so versioned domain-action endpoints apply the
    // SAME shared configuration (auth, permissions, rate-limit, caching, metadata) to EVERY mapped
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
                    RenderPermissionRequirement(_model.Authorization.RequiredPermissions.AsImmutableArray(), "All", b);
                }
                else if (!_model.Authorization.AnyPermissions.IsDefaultOrEmpty)
                {
                    RenderPermissionRequirement(_model.Authorization.AnyPermissions.AsImmutableArray(), "Any", b);
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
        if (_model.HasFormParams || _model.IsAttachmentUpload)
        {
            if (!_model.RequireAntiforgery)
                AppendLine($"{b}.DisableAntiforgery();");
            if (_model.IsAttachmentUpload || _model.FormParameters.Any(p => p.IsFile))
                AppendLine(EndpointMetadataRenderer.RenderAccepts(b, "Microsoft.AspNetCore.Http.IFormFile", "multipart/form-data"));

            // Cap the request at the configured limit. Without this the server buffers the whole body —
            // up to Kestrel's 30 MB, or FormOptions' 128 MB for the multipart section — onto a temp file
            // BEFORE the handler can answer 413, so a small MaxFileSizeBytes bought no protection at all
            // and a repeated oversized upload filled the temp path.
            var uploadLimit = _model.AttachmentUpload?.MaxFileSizeBytes ?? 0;
            if (uploadLimit > 0)
            {
                AppendLine($"{b}.WithMetadata(new global::Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute({uploadLimit}L));");
                AppendLine($"{b}.WithMetadata(new global::Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute {{ MultipartBodyLengthLimit = {uploadLimit}L }});");
            }
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

    // Honor Location/NoStore with real HTTP cache-control semantics (see EndpointHandlerTemplate).
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

        var hasVaryBy = !cache.VaryByQueryKeys.IsDefaultOrEmpty || !cache.VaryByHeaders.IsDefaultOrEmpty;
        var hasProfile = !string.IsNullOrEmpty(cache.Profile);

        if (!hasVaryBy && !hasProfile)
        {
            AppendLine($"{b}.CacheOutput(c => c.Expire(System.TimeSpan.FromSeconds({cache.Duration})));");
            return;
        }

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

    private void RenderPermissionRequirement(System.Collections.Immutable.ImmutableArray<string> permissions, string mode, string b = "builder")
    {
        var permArray = string.Join(", ", permissions.Select(p => $"\"{p}\""));

        AppendLine($"{b}.RequireAuthorization(policy => policy.AddRequirements(");
        IncreaseIndent();
        AppendLine("new global::Pragmatic.Endpoints.Authorization.PragmaticPermissionRequirement(");
        IncreaseIndent();
        AppendLine($"new[] {{ {permArray} }},");
        AppendLine($"global::Pragmatic.Endpoints.Authorization.PermissionMode.{mode})));");
        DecreaseIndent();
        DecreaseIndent();
    }

    private void RenderProducesMetadata(string b = "builder")
    {
        // Success response
        if (_model.IsStreamingResponse)
        {
            AppendLine(EndpointMetadataRenderer.RenderProduces(b, _model.StreamItemTypeName!, 200, "text/event-stream"));
        }
        else if (_model.IsVoidDomainAction)
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
        else if (!string.IsNullOrEmpty(_model.DomainActionReturnType))
        {
            var successCode = _model.ComputedSuccessStatusCode;
            AppendLine(EndpointMetadataRenderer.RenderProduces(b, _model.DomainActionReturnType!, successCode));
        }

        // Deduplicated error contract: 400/500 fallback plus the auth rejections the pipeline
        // actually produces — so the OpenAPI document matches runtime behavior.
        var codes = new SortedSet<int> { 400, 500 };
        // Auth applies by default (RequireAuthorizationByDefault at the root group) — only
        // [AllowAnonymous] opts out.
        if (_model.Authorization is not { AllowAnonymous: true })
        {
            codes.Add(401);
            codes.Add(403);
        }

        // The errors the action declares on its base type. This loop was missing, so an action written
        // as VoidDomainAction<ConflictError> documented 400/401/403/500 and not the 409 it returns,
        // while the identical declaration on a Mutation or an [Endpoint] class documented it —
        // the same contract described two different ways depending on which base the author picked.
        //
        // ProblemDetails rather than Produces<TError>: the body on the wire is what
        // ErrorExtensions.ToResult writes, and that is a ProblemDetails whatever the error type is.
        foreach (var error in _model.ErrorTypes)
            codes.Add(error.StatusCode);

        foreach (var code in codes)
            AppendLine(EndpointMetadataRenderer.RenderProducesProblem(b, code));
    }
}
