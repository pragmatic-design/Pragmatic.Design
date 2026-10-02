using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

internal sealed partial class QueryHandlerTemplate
{
    private void RenderEndpointConfiguration()
    {
        if (!string.IsNullOrEmpty(_model.Name))
            AppendLine($"builder.WithName(\"{EscapeString(_model.Name)}\");");

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
            var entityName = (_model.MutationEntityType ?? _model.QueryEntityType)?.Split('.').LastOrDefault();
            var defaultTag = EndpointTagHelper.DeriveTagFromRoute(_model.Route, entityName);
            if (defaultTag is not null)
                AppendLine($"builder.WithTags(\"{defaultTag}\");");
        }

        // A route that belongs to no tenant says so, and the tenant middleware reads it. Separate
        // from the authorization block below because the two are separate questions:
        // [AllowAnonymous] lifts authentication, and the tenant refusal happens at order 92, before
        // the route runs and whoever is asking — which is why an anonymous probe was answered 400 in
        // a multi-tenant host.
        if (_model.Authorization?.RouteIsTenantAgnostic == true)
            AppendLine(EndpointMetadataRenderer.RenderTenantAgnostic("builder"));

        if (_model.Authorization is not null)
        {
            if (_model.Authorization.AllowAnonymous)
                AppendLine("builder.AllowAnonymous();");
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

        // API Versioning
        if (!_model.ApiVersions.IsDefaultOrEmpty)
            foreach (var version in _model.ApiVersions)
                if (version.Deprecated)
                    AppendLine(
                        $"builder.WithMetadata(new Microsoft.AspNetCore.Mvc.ApiVersionAttribute(\"{version.Version}\") {{ Deprecated = true }});");

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

        // What the route accepts, when the query takes its filters as a body. Without it the published
        // document describes a request with no body at all, and a client generated from it cannot send
        // the one thing the route exists to read.
        if (_model.GridRequestPropertyName is not null)
        {
            AppendLine(EndpointMetadataRenderer.RenderAccepts(
                "builder", "global::Pragmatic.Persistence.Query.Adapters.GridFilterRequest", "application/json"));
        }

        // Produces metadata
        if (!string.IsNullOrEmpty(_model.ResponseType))
            AppendLine(EndpointMetadataRenderer.RenderProduces("builder", _model.ResponseType!, 200));
        AppendLine(EndpointMetadataRenderer.RenderProducesProblem("builder", 400));
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
}
