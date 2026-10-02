using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pragmatic.Identity;

/// <summary>
///     Development middleware that populates <see cref="ICurrentUser"/> from HTTP headers.
///     Reads <c>X-User-Id</c> and <c>X-User-Name</c> headers and creates a
///     <see cref="ClaimsPrincipal"/> so that <see cref="ClaimsPrincipalUserAccessor"/>
///     can resolve the current user without a real IdP.
/// </summary>
/// <remarks>
///     Must run BEFORE <c>UseAuthentication()</c> in the pipeline.
///     Intended for development and integration testing only.
///     Throws <see cref="InvalidOperationException"/> if registered in a non-Development environment.
/// </remarks>
public sealed partial class HeaderUserMiddleware(
    RequestDelegate next,
    IHostEnvironment environment,
    ILogger<HeaderUserMiddleware> logger)
{
    private const string UserIdHeader = "X-User-Id";
    private const string UserNameHeader = "X-User-Name";
    private const string UserRolesHeader = "X-User-Roles";
    private const string UserPermissionsHeader = "X-User-Permissions";
    private const string UserTenantHeader = "X-User-Tenant";
    private const string UserGroupsHeader = "X-User-Groups";

    /// <summary>
    ///     Data scopes, comma-separated — the claim <c>DefaultUserScopeResolver</c> turns into
    ///     <c>scope:{value}</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Added because the resolver read a claim no transport here could carry.
    ///     <c>ComputedScopeFilter</c> keeps the rules whose <c>scope:{ScopeName}</c> the caller has,
    ///     and the only scopes a header identity could produce were <c>user:</c> and <c>role:</c> —
    ///     so a <c>DataScopeRule</c> with <c>ScopeStrategy.Computed</c> matched nothing at all, in
    ///     Development and in every test that authenticates this way. The claim type is not
    ///     configurable on <c>IdentityOptions</c> the way roles and permissions are; it is the
    ///     resolver's own constant, and this uses the same one.
    /// </remarks>
    private const string UserScopesHeader = "X-User-Scopes";

    /// <summary>The claim type <c>DefaultUserScopeResolver</c> reads data scopes from.</summary>
    private const string DataScopeClaimType = "data-scope";

    /// <summary>
    ///     Supplies <c>ICurrentUser.Authentication.ExternalIdentityKey</c> directly.
    /// </summary>
    /// <remarks>
    ///     Without it nothing that resolves a user from the database works behind this middleware: the
    ///     key is otherwise composed from <c>iss</c> and the subject claim, and this emits neither —
    ///     the id goes in as <c>UserIdClaimType</c>, which is not <c>sub</c>. Every feature reached
    ///     through a user resolver, per-user culture among them, would be untestable end to end
    ///     without a real IdP.
    /// </remarks>
    private const string ExternalIdentityKeyHeader = "X-User-External-Key";

    /// <summary>
    ///     Headers that make the request a delegated one: <c>X-Act-*</c> beside <c>X-User-*</c>, the
    ///     same split as the token they stand in for — the user headers name the subject, these name
    ///     who is acting for them.
    /// </summary>
    /// <remarks>
    ///     Without these the request-borne delegation path is unreachable in development, which is
    ///     every local run and every integration test: the framework reads <c>act_sub</c> and the rest
    ///     off a token it does not mint, so an application could only find out whether delegation
    ///     works after wiring a real IdP. That is the shape this repository keeps meeting — a
    ///     mechanism whose only entry point is somewhere nobody can reach while building.
    /// </remarks>
    private const string ActorIdHeader = "X-Act-Id";
    private const string ActorKindHeader = "X-Act-Kind";
    private const string ActorPolicyHeader = "X-Act-Policy";
    private const string ActorPurposeHeader = "X-Act-Purpose";
    private const string ActorGrantHeader = "X-Act-Grant";
    private const string ActorChainHeader = "X-Act-Chain";
    private const string ActorPermissionsHeader = "X-Act-Permissions";
    private const string ActorGrantPermissionsHeader = "X-Act-Grant-Permissions";

    public async Task InvokeAsync(HttpContext context, IOptions<IdentityOptions> identityOptions)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException(
                $"{nameof(HeaderUserMiddleware)} is a development-only middleware and must not be registered in the '{environment.EnvironmentName}' environment. " +
                "Remove it from the production pipeline or guard its registration with env.IsDevelopment().");

        if (context.Request.Headers.TryGetValue(UserIdHeader, out var userId) &&
            !string.IsNullOrEmpty(userId.ToString()))
        {
            var opts = identityOptions.Value;
            var userName = context.Request.Headers.TryGetValue(UserNameHeader, out var name)
                ? name.ToString()
                : "Unknown";

            var claims = new List<Claim>
            {
                new(opts.UserIdClaimType, userId.ToString()),
                new(opts.DisplayNameClaimType, userName)
            };

            AddHeaderClaims(context, claims, UserRolesHeader, opts.RoleClaimType);
            AddHeaderClaims(context, claims, UserPermissionsHeader, opts.PermissionClaimType);
            AddHeaderClaim(context, claims, UserTenantHeader, opts.TenantClaimType);
            AddHeaderClaim(context, claims, ExternalIdentityKeyHeader, ExternalIdentityKey.ClaimType);
            AddHeaderClaims(context, claims, UserGroupsHeader, "group");
            AddHeaderClaims(context, claims, UserScopesHeader, DataScopeClaimType);

            AddDelegationClaims(context, claims);

            var identity = new ClaimsIdentity(claims, "HeaderAuth");
            context.User = new ClaimsPrincipal(identity);

            LogUserResolved(userId.ToString(), context.Request.Path);
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    ///     Adds the delegation claims, and only when <c>X-Act-Id</c> names an actor.
    /// </summary>
    /// <remarks>
    ///     The actor gates the rest deliberately. A request carrying a policy or a purpose but no
    ///     actor is not a delegation, and emitting those claims alone would leave a principal that
    ///     reads as delegated to some code and not to others.
    /// </remarks>
    private static void AddDelegationClaims(HttpContext context, List<Claim> claims)
    {
        if (!context.Request.Headers.TryGetValue(ActorIdHeader, out var actorId)
            || string.IsNullOrEmpty(actorId.ToString()))
            return;

        claims.Add(new Claim(DelegationClaims.ActorSubject, actorId.ToString()));

        AddHeaderClaim(context, claims, ActorKindHeader, DelegationClaims.ActorKind);
        AddHeaderClaim(context, claims, ActorPolicyHeader, DelegationClaims.Policy);
        AddHeaderClaim(context, claims, ActorPurposeHeader, DelegationClaims.Purpose);
        AddHeaderClaim(context, claims, ActorGrantHeader, DelegationClaims.GrantId);
        AddHeaderClaim(context, claims, ActorChainHeader, DelegationClaims.Chain);

        // Multi-valued, like the subject's own permissions: the resolver reads every matching claim.
        AddHeaderClaims(context, claims, ActorPermissionsHeader, DelegationClaims.ActorPermissions);
        AddHeaderClaims(context, claims, ActorGrantPermissionsHeader, DelegationClaims.GrantPermissions);
    }

    /// <summary>Adds a single-value header as a claim.</summary>
    private static void AddHeaderClaim(HttpContext context, List<Claim> claims, string headerName, string claimType)
    {
        if (context.Request.Headers.TryGetValue(headerName, out var value) &&
            !string.IsNullOrEmpty(value.ToString()))
        {
            claims.Add(new Claim(claimType, value.ToString()));
        }
    }

    /// <summary>Adds comma-separated header values as individual claims.</summary>
    private static void AddHeaderClaims(HttpContext context, List<Claim> claims, string headerName, string claimType)
    {
        if (!context.Request.Headers.TryGetValue(headerName, out var value))
            return;

        foreach (var item in value.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(claimType, item));
        }
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "User '{UserId}' resolved from headers for {RequestPath}")]
    private partial void LogUserResolved(string userId, PathString requestPath);
}
