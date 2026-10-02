using System.Collections.Generic;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Endpoint configuration, authorization, and OpenAPI metadata for Mutation endpoint handlers.
/// </summary>
internal sealed partial class MutationHandlerTemplate
{
    private void RenderEndpointConfiguration()
    {
        // Name
        if (!string.IsNullOrEmpty(_model.Name))
            AppendLine($"builder.WithName(\"{EscapeString(_model.Name)}\");");

        // OpenAPI
        if (!string.IsNullOrEmpty(_model.Summary))
            AppendLine($"builder.WithSummary(\"{EscapeString(_model.Summary!)}\");");

        if (!string.IsNullOrEmpty(_model.Description))
            AppendLine($"builder.WithDescription(\"{EscapeString(_model.Description!)}\");");

        if (!_model.Tags.IsDefaultOrEmpty)
        {
            var tags = string.Join(", ", _model.Tags.Select(t => $"\"{t}\""));
            AppendLine($"builder.WithTags({tags});");
        }
        else
        {
            // Default tag derived from route (or entity name fallback for relative routes)
            var entityName = _model.MutationEntityType?.Split('.').LastOrDefault();
            var defaultTag = EndpointTagHelper.DeriveTagFromRoute(_model.Route, entityName);
            if (defaultTag is not null)
                AppendLine($"builder.WithTags(\"{defaultTag}\");");
        }

        // A route that belongs to no tenant says so, and the tenant middleware reads it. Emitted
        // before the authorization block because the two are separate questions: [AllowAnonymous]
        // lifts authentication, and the tenant refusal happens at order 92, before the route runs and
        // whoever is asking — which is why an anonymous probe was answered 400 in a multi-tenant host.
        if (_model.Authorization?.RouteIsTenantAgnostic == true)
            AppendLine(EndpointMetadataRenderer.RenderTenantAgnostic("builder"));

        // Authorization
        if (_model.Authorization is not null)
        {
            if (_model.Authorization.AllowAnonymous)
            {
                AppendLine("builder.AllowAnonymous();");
            }
            else if (_model.Authorization.IsRequired)
            {
                if (!string.IsNullOrEmpty(_model.Authorization.PolicyName))
                {
                    AppendLine($"builder.RequireAuthorization(\"{StringHelper.CSharpLiteral(_model.Authorization.PolicyName)}\");");
                }
                else if (!_model.Authorization.RequiredPermissions.IsDefaultOrEmpty)
                {
                    RenderPermissionRequirement(_model.Authorization.RequiredPermissions.AsImmutableArray(), "All");
                }
                else if (!_model.Authorization.AnyPermissions.IsDefaultOrEmpty)
                {
                    RenderPermissionRequirement(_model.Authorization.AnyPermissions.AsImmutableArray(), "Any");
                }
                else
                {
                    AppendLine("builder.RequireAuthorization();");
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
            AppendLine($"builder.RequireRateLimiting(\"{StringHelper.CSharpLiteral(rateLimitPolicy)}\");");

        // Request body size limit ([MaxBodySize]) — enforced by RequestLimitsStep before the body is read
        if (_model.MaxBodySizeBytes is > 0)
            AppendLine($"builder.WithMetadata(new global::Pragmatic.Http.MaxBodySizeMetadata({_model.MaxBodySizeBytes}L));");

        // Response caching
        if (_model.ResponseCache is not null)
            RenderCacheOutput(_model.ResponseCache);

        // Antiforgery ([RequireAntiforgery]) — enforced by the antiforgery middleware
        if (_model.RequireAntiforgery)
            AppendLine("builder.WithMetadata(new Microsoft.AspNetCore.Antiforgery.RequireAntiforgeryTokenAttribute());");

        // Idempotency ([Idempotent]) — replay cache keyed on header + body hash
        foreach (var line in IdempotencyFilterHelper.RenderLines(_model, "builder"))
            AppendLine(line);

        // Form data configuration — [RequireAntiforgery] keeps token validation on
        if (_model.HasFormParams)
        {
            if (!_model.RequireAntiforgery)
                AppendLine("builder.DisableAntiforgery();");
            if (_model.FormParameters.Any(p => p.IsFile))
                AppendLine(EndpointMetadataRenderer.RenderAccepts("builder", "Microsoft.AspNetCore.Http.IFormFile", "multipart/form-data"));
        }

        // Produces metadata for OpenAPI
        // The request body, for OpenAPI. A generated endpoint is mapped as a RequestDelegate so it
        // survives an AOT publish, and a RequestDelegate has no typed parameters — so nothing can infer
        // what it accepts. Only the multipart case ever said so, which left the document describing the
        // responses of a POST and not its body.
        if (_model.NeedsBodyDto && !_model.HasFormParams)
        {
            foreach (var variant in _model.BodyDtoVariants)
            {
                AppendLine(EndpointMetadataRenderer.RenderAccepts(
                    "builder", $"global::{_model.Namespace}.{variant.Name}", "application/json"));
            }
        }

        RenderProducesMetadata();
    }

    // Honor Location/NoStore with real HTTP cache-control semantics (see EndpointHandlerTemplate).
    private void RenderCacheOutput(Models.ResponseCacheModel cache)
    {
        var location = cache.Location ?? "Any";

        if (cache.NoStore || location == "None")
        {
            RenderCacheControlHeader("no-store");
            return;
        }

        if (location == "Client")
        {
            var value = cache.Duration > 0 ? $"private, max-age={cache.Duration}" : "private";
            RenderCacheControlHeader(value);
            return;
        }

        var hasVaryBy = !cache.VaryByQueryKeys.IsDefaultOrEmpty || !cache.VaryByHeaders.IsDefaultOrEmpty;
        var hasProfile = !string.IsNullOrEmpty(cache.Profile);

        if (!hasVaryBy && !hasProfile)
        {
            AppendLine($"builder.CacheOutput(c => c.Expire(System.TimeSpan.FromSeconds({cache.Duration})));");
            return;
        }

        AppendLine("builder.CacheOutput(c =>");
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

    private void RenderCacheControlHeader(string headerValue)
    {
        AppendLine("builder.AddEndpointFilter(async (cacheCtx, cacheNext) =>");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"cacheCtx.HttpContext.Response.Headers.CacheControl = \"{headerValue}\";");
        AppendLine("return await cacheNext(cacheCtx);");
        DecreaseIndent();
        AppendLine("});");
    }

    private void RenderPermissionRequirement(System.Collections.Immutable.ImmutableArray<string> permissions, string mode)
    {
        var permArray = string.Join(", ", permissions.Select(p => $"\"{p}\""));

        AppendLine("builder.RequireAuthorization(policy => policy.AddRequirements(");
        IncreaseIndent();
        AppendLine("new global::Pragmatic.Endpoints.Authorization.PragmaticPermissionRequirement(");
        IncreaseIndent();
        AppendLine($"new[] {{ {permArray} }},");
        AppendLine($"global::Pragmatic.Endpoints.Authorization.PermissionMode.{mode})));");
        DecreaseIndent();
        DecreaseIndent();
    }

    private void RenderProducesMetadata()
    {
        // Success response — mutations always return the entity type
        if (!string.IsNullOrEmpty(_model.ResponseType))
        {
            var successCode = _model.ComputedSuccessStatusCode;
            AppendLine(EndpointMetadataRenderer.RenderProduces("builder", _model.ResponseType!, successCode));
        }

        // The statuses come from the model, not from here. Computed in this method, only the runtime
        // metadata would have them: the manifest — and therefore the compile-time OpenAPI document
        // that ships at /openapi/v1.json — would list a single 200 for a create that answers 201 and
        // can answer 400, 401, 403, 409 and 500.
        foreach (var code in _model.MutationProblemStatusCodes)
            AppendLine(EndpointMetadataRenderer.RenderProducesProblem("builder", code));
    }
}
