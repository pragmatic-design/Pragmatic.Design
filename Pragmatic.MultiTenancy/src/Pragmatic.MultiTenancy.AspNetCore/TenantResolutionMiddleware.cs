using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Result;

namespace Pragmatic.MultiTenancy;

/// <summary>
///     Middleware that resolves the current tenant early in the HTTP pipeline.
///     Must run before endpoint routing so that tenant context is available to all downstream components.
/// </summary>
/// <remarks>
///     Add to the pipeline:
///     <code>app.UseMiddleware&lt;TenantResolutionMiddleware&gt;();</code>
/// </remarks>
public sealed partial class TenantResolutionMiddleware(
    RequestDelegate next,
    ILogger<TenantResolutionMiddleware> logger,
    IOptions<MultiTenancyOptions> options)
{
    /// <summary>
    ///     Resolves the tenant using the registered <see cref="ITenantResolver" /> and populates
    ///     the scoped <see cref="MutableTenantContext" />.
    ///     When <see cref="MultiTenancyOptions.RequireTenant" /> is <c>true</c> and no tenant is
    ///     resolved, the request is short-circuited with HTTP 400 and a ProblemDetails whose <c>code</c> is
    ///     <see cref="TenantRefusalCodes.Required" /> — unless the matched endpoint carries <see cref="TenantAgnosticEndpoint" />,
    ///     which is how the routes that belong to no tenant (a liveness probe, the published contract) stay reachable
    ///     to callers that have none, or the caller is not authenticated on a route authorization will refuse it,
    ///     which then answers 401.
    /// </summary>
    /// <param name="context">The current HTTP request.</param>
    /// <param name="resolver">The registered resolution strategy.</param>
    /// <param name="tenantContext">The scoped context this middleware populates.</param>
    /// <param name="tenantStore">
    ///     Optional. Present only when the application registers one; it is the authority on tenant
    ///     state, and where there is none the state check does not apply.
    /// </param>
    public async Task InvokeAsync(
        HttpContext context,
        ITenantResolver resolver,
        MutableTenantContext tenantContext,
        ITenantStore? tenantStore = null)
    {
        // Routing runs at step 50 and this at 92, so the endpoint is already matched and its metadata
        // readable. An endpoint that declares itself tenant-agnostic still gets its tenant resolved
        // when one is supplied — only the refusal below is lifted.
        var isTenantAgnostic = context.GetEndpoint()?.Metadata
            .GetMetadata<TenantAgnosticEndpoint>() is not null;

        var tenantId = await resolver.ResolveAsync(context.RequestAborted).ConfigureAwait(false);

        // A route that runs in no tenant (#724): a tenant the request brings is refused, whatever resolved it, rather than served in, and
        // without one the route runs with none, RequireTenant notwithstanding. Before the claim guard, since no tenant is the caller's here.
        if (context.GetEndpoint()?.Metadata.GetMetadata<NoTenantEndpoint>() is not null)
        {
            if (!string.IsNullOrEmpty(tenantId))
            {
                LogTenantNotAllowed(tenantId, context.Request.Path);
                await RefuseAsync(context, StatusCodes.Status400BadRequest, TenantRefusalCodes.NotAllowed,
                    "The operation runs in no tenant, and the request names one.").ConfigureAwait(false);
                return;
            }

            await next(context).ConfigureAwait(false);
            return;
        }

        // Fail-closed claim guard: for an authenticated user carrying a tenant claim, the tenant
        // resolved by ANY strategy (header/route/subdomain/custom) must equal the claim value.
        // This prevents cross-tenant escalation via client-controlled request input.
        // No claim present (anonymous / pre-auth) => the resolved value is kept as-is.
        if (!string.IsNullOrEmpty(tenantId) && options.Value.EnforceTenantClaim)
        {
            var claimTenant = context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirst(options.Value.TenantClaimType)?.Value
                : null;

            if (!string.IsNullOrEmpty(claimTenant) &&
                !string.Equals(claimTenant, tenantId, StringComparison.Ordinal))
            {
                LogTenantClaimMismatch(tenantId, claimTenant, context.Request.Path);
                await RefuseAsync(context, StatusCodes.Status403Forbidden, TenantRefusalCodes.Mismatch,
                    "The tenant of the request is not the caller's.").ConfigureAwait(false);
                return;
            }
        }

        // The tenant's state decides whether it is served at all. Without this the enum is written
        // and never read: suspending a tenant, deactivating it, or catching one still provisioning
        // would record a state and serve the requests anyway.
        // A suspended tenant may not be served its data; it may still be told whether the host is
        // alive. The state of one tenant is not a fact about a route that belongs to none.
        if (!string.IsNullOrEmpty(tenantId)
            && !isTenantAgnostic
            && (options.Value.EnforceTenantState || options.Value.RequireKnownTenant)
            && tenantStore is not null)
        {
            var tenant = await tenantStore.GetByIdAsync(tenantId, context.RequestAborted).ConfigureAwait(false);

            if (tenant is not null && options.Value.EnforceTenantState && tenant.State != TenantState.Active)
            {
                LogTenantNotActive(tenantId, tenant.State.ToString(), context.Request.Path);
                var suspended = tenant.State == TenantState.Suspended;
                await RefuseAsync(context, StatusCodes.Status403Forbidden,
                    suspended ? TenantRefusalCodes.Suspended : TenantRefusalCodes.NotActive,
                    suspended ? "The tenant is suspended." : "The tenant is not active.",
                    tenant.State.ToString()).ConfigureAwait(false);
                return;
            }

            // 404, not 403: an unknown tenant is not a forbidden one, and answering "forbidden" would
            // confirm that the id names something. Separate switch from the state check because it
            // asks a stronger question — whether the store enumerates every tenant that may be served.
            if (tenant is null && options.Value.RequireKnownTenant)
            {
                LogTenantUnknown(tenantId, context.Request.Path);
                await RefuseAsync(context, StatusCodes.Status404NotFound, TenantRefusalCodes.Unknown,
                    "No such tenant.").ConfigureAwait(false);
                return;
            }
        }

        if (!string.IsNullOrEmpty(tenantId))
        {
            tenantContext.TenantId = tenantId;
            // TenantName is left null — the resolver only resolves the ID.
            // Downstream code that needs the name should resolve it from ITenantStore.
            LogTenantResolved(tenantId, context.Request.Path);
        }
        else
        {
            LogTenantNotResolved(context.Request.Path);

            if (options.Value.RequireTenant && !isTenantAgnostic)
            {
                // A caller who has not signed in, on a route that requires it, is told to sign in: when the tenant comes
                // from the token, no token is no tenant, and a 400 here would stand in front of the 401 authorization
                // answers. Let through only where authorization will refuse it, so no route runs without a tenant.
                if (context.User.Identity?.IsAuthenticated != true
                    && await AuthorizationWillChallengeAsync(context).ConfigureAwait(false))
                {
                    LogUnauthenticatedWithoutTenant(context.Request.Path);
                    await next(context).ConfigureAwait(false);
                    return;
                }

                await RefuseAsync(context, StatusCodes.Status400BadRequest, TenantRefusalCodes.Required,
                    "The request does not say which tenant it is for.").ConfigureAwait(false);
                return;
            }
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    ///     Writes a refusal as a ProblemDetails carrying its <paramref name="code" />, with the title and detail the host's
    ///     <see cref="IErrorMessageResolver" /> gives for that code, or the defaults when it gives none.
    /// </summary>
    private static Task RefuseAsync(HttpContext context, int status, string code, string detail, string? tenantState = null)
    {
        var messages = context.RequestServices?.GetService<IErrorMessageResolver>();
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (tenantState is not null)
            extensions["tenantState"] = tenantState;

        return TypedResults.Problem(
                detail: messages?.Resolve(code) ?? detail,
                statusCode: status,
                title: messages?.ResolveTitle(code),
                extensions: extensions)
            .ExecuteAsync(context);
    }

    /// <summary>
    ///     Whether the authorization middleware will refuse an anonymous caller on this endpoint: it declares an
    ///     authorization requirement, or the host has a fallback policy, and it does not allow anonymous callers.
    /// </summary>
    private static async Task<bool> AuthorizationWillChallengeAsync(HttpContext context)
    {
        var metadata = context.GetEndpoint()?.Metadata;
        if (metadata?.GetMetadata<IAllowAnonymous>() is not null)
            return false;

        if (metadata?.GetMetadata<IAuthorizeData>() is not null)
            return true;

        var policies = context.RequestServices?.GetService<IAuthorizationPolicyProvider>();
        return policies is not null && await policies.GetFallbackPolicyAsync().ConfigureAwait(false) is not null;
    }

    // =========================================================================
    // Logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "No tenant resolved for the unauthenticated request {RequestPath}; passed on, for authorization to challenge it")]
    private partial void LogUnauthenticatedWithoutTenant(PathString requestPath);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Tenant '{TenantId}' resolved for {RequestPath}, which runs in no tenant. Rejecting with 400.")]
    private partial void LogTenantNotAllowed(string tenantId, PathString requestPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tenant '{TenantId}' resolved for {RequestPath}")]
    private partial void LogTenantResolved(string tenantId, PathString requestPath);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Tenant claim mismatch: request resolved tenant '{ResolvedTenant}' but authenticated user's tenant claim is '{ClaimTenant}' for {RequestPath}. Rejecting request (403) to prevent cross-tenant access.")]
    private partial void LogTenantClaimMismatch(string resolvedTenant, string claimTenant, PathString requestPath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "No tenant resolved for {RequestPath}")]
    private partial void LogTenantNotResolved(PathString requestPath);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Tenant '{TenantId}' is in state {TenantState}, not Active. Rejecting {RequestPath} with 403.")]
    private partial void LogTenantNotActive(string tenantId, string tenantState, PathString requestPath);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Tenant '{TenantId}' is not in the tenant store. Rejecting {RequestPath} with 404.")]
    private partial void LogTenantUnknown(string tenantId, PathString requestPath);
}
